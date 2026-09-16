namespace Cerberus.Application.Oidc;

public sealed record AuthorizationRequestDto(
    Guid UserId,
    Guid? SessionId,
    string ClientId,
    IReadOnlyList<string> Scopes,
    string? Organization,
    bool PromptConsent);

public enum AuthorizationOutcome
{
    Granted = 0,
    Rejected = 1,
    OrganizationSelectionRequired = 2,
    ConsentRequired = 3
}

public sealed record OrganizationOptionDto(Guid Id, string Slug, string Name);

public sealed record ScopeDescriptionDto(string Name, string DisplayName, string? Description);

public sealed record ConsentPromptDto(string ClientDisplayName, OrganizationOptionDto Organization, IReadOnlyList<ScopeDescriptionDto> Scopes);

public sealed record AuthorizationEvaluationDto(
    AuthorizationOutcome Outcome,
    TokenSubject? Subject = null,
    string? Error = null,
    string? ErrorDescription = null,
    IReadOnlyList<OrganizationOptionDto>? OrganizationOptions = null,
    ConsentPromptDto? Consent = null)
{
    public static AuthorizationEvaluationDto Reject(string error, string description) => new(AuthorizationOutcome.Rejected, Error: error, ErrorDescription: description);
}

public static class OidcErrors
{
    public const string AccessDenied = "access_denied";
    public const string ConsentRequired = "consent_required";
    public const string InvalidRequest = "invalid_request";
    public const string InvalidScope = "invalid_scope";
    public const string UnauthorizedClient = "unauthorized_client";
    public const string InvalidGrant = "invalid_grant";
}

/// <summary>Custom authorization request parameter carrying the organization requested by the client (slug or id).</summary>
public static class OidcParameters
{
    public const string Organization = "organization";
}
