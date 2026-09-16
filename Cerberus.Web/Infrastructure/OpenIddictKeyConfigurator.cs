using Cerberus.Domain.Keys;
using Cerberus.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Cerberus.Web.Infrastructure;

/// <summary>
/// Loads persisted keys into the OpenIddict server options when they are first materialized (after database
/// initialization). The active key is registered first so it is used to sign and encrypt; pending and retired keys are
/// only published in JWKS.
/// </summary>
public sealed class OpenIddictKeyConfigurator(IServiceScopeFactory scopeFactory, KeyRotationHostedService rotationService)
    : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        using var scope = scopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<SigningKeyManager>();

        var signingKeys = manager.LoadKeysAsync(KeyUsage.Signing).GetAwaiter().GetResult();
        foreach (var key in signingKeys)
        {
            options.SigningCredentials.Add(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        }

        foreach (var key in manager.LoadKeysAsync(KeyUsage.Encryption).GetAwaiter().GetResult())
        {
            options.EncryptionCredentials.Add(new EncryptingCredentials(key, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
        }

        rotationService.LoadedSigningKeyIds = signingKeys.Select(k => k.KeyId).ToList();
    }
}
