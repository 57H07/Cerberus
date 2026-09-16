using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cerberus.IntegrationTests.Infrastructure;

/// <summary>Drives the authorization code + PKCE flow like a browser and a confidential client would.</summary>
public sealed partial class OidcFlow(HttpClient browser, TestScenario scenario)
{
    public string CodeVerifier { get; private set; } = NewVerifier();
    public string State { get; } = Guid.NewGuid().ToString("N");
    public string Nonce { get; } = Guid.NewGuid().ToString("N");

    public HttpClient Browser => browser;

    public async Task<HttpResponseMessage> LoginAsync(string login, string password = TestScenario.Password)
    {
        var page = await browser.GetAsync("/Account/Login");
        var token = ExtractAntiforgery(await page.Content.ReadAsStringAsync());
        return await browser.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Login"] = login,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
    }

    public string AuthorizeUrl(string scope = "openid profile", string? organization = null, string? redirectUri = null,
        bool pkce = true, string? prompt = null, string? clientId = null)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = clientId ?? scenario.ClientId,
            ["redirect_uri"] = redirectUri ?? scenario.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = State,
            ["nonce"] = Nonce
        };
        if (pkce)
        {
            parameters["code_challenge"] = Challenge(CodeVerifier);
            parameters["code_challenge_method"] = "S256";
        }

        if (organization is not null)
        {
            parameters["organization"] = organization;
        }

        if (prompt is not null)
        {
            parameters["prompt"] = prompt;
        }

        return QueryHelpers.AddQueryString("/connect/authorize", parameters);
    }

    /// <summary>Follows local redirects and returns the final response (a page, or a redirect to the client).</summary>
    public async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage response)
    {
        for (var i = 0; i < 10 && response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther; i++)
        {
            var location = response.Headers.Location!;
            if (location.IsAbsoluteUri && location.Host != "localhost")
            {
                return response;
            }

            response = await browser.GetAsync(location);
        }

        return response;
    }

    public async Task<HttpResponseMessage> AuthorizeAsync(string url) => await FollowAsync(await browser.GetAsync(url));

    public async Task<HttpResponseMessage> SubmitFormAsync(HttpResponseMessage page, string submit, IDictionary<string, string>? overrides = null)
    {
        var html = await page.Content.ReadAsStringAsync();
        var fields = HiddenInputPattern().Matches(html)
            .Select(m => new KeyValuePair<string, string>(WebUtility.HtmlDecode(m.Groups["name"].Value), WebUtility.HtmlDecode(m.Groups["value"].Value)))
            .ToList();
        fields.Add(new("submit", submit));
        fields.Add(new("__RequestVerificationToken", ExtractAntiforgery(html)));
        foreach (var (key, value) in overrides ?? new Dictionary<string, string>())
        {
            fields.RemoveAll(f => f.Key == key);
            fields.Add(new(key, value));
        }

        return await FollowAsync(await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields)));
    }

    public static IReadOnlyDictionary<string, string> RedirectParameters(HttpResponseMessage response)
    {
        var location = response.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect, got {(int)response.StatusCode}.");
        return QueryHelpers.ParseQuery(location.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    public async Task<string> AuthorizeAndGetCodeAsync(string scope = "openid profile", string? organization = null)
    {
        var response = await AuthorizeAsync(AuthorizeUrl(scope, organization));
        if (response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("value=\"accept\""))
        {
            response = await SubmitFormAsync(response, "accept");
        }

        var parameters = RedirectParameters(response);
        parameters.Should().ContainKey("code", because: $"authorization should succeed but got {string.Join(", ", parameters)}");
        parameters["state"].Should().Be(State);
        return parameters["code"];
    }

    public async Task<TokenResponse> RedeemAsync(string code, string? codeVerifier = null, string? clientSecret = null)
    {
        var response = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = scenario.RedirectUri,
            ["client_id"] = scenario.ClientId,
            ["client_secret"] = clientSecret ?? scenario.ClientSecret,
            ["code_verifier"] = codeVerifier ?? CodeVerifier
        }));
        return await TokenResponse.ReadAsync(response);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken)
    {
        var response = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = scenario.ClientId,
            ["client_secret"] = scenario.ClientSecret
        }));
        return await TokenResponse.ReadAsync(response);
    }

    public void ResetPkce() => CodeVerifier = NewVerifier();

    public static string ExtractAntiforgery(string html)
        => WebUtility.HtmlDecode(AntiforgeryPattern().Match(html).Groups["value"].Value);

    public static JsonWebToken ReadJwt(string token) => new(token);

    private static string NewVerifier() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    [GeneratedRegex("<input type=\"hidden\" name=\"(?<name>[^\"]+)\" value=\"(?<value>[^\"]*)\"")]
    private static partial Regex HiddenInputPattern();
}

public sealed record TokenResponse(HttpStatusCode StatusCode, JsonElement Json)
{
    public bool Succeeded => StatusCode == HttpStatusCode.OK;
    public string? Error => Json.TryGetProperty("error", out var value) ? value.GetString() : null;
    public string AccessToken => Json.GetProperty("access_token").GetString()!;
    public string IdToken => Json.GetProperty("id_token").GetString()!;
    public string? RefreshToken => Json.TryGetProperty("refresh_token", out var value) ? value.GetString() : null;

    public static async Task<TokenResponse> ReadAsync(HttpResponseMessage response)
        => new(response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
}
