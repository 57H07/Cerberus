using System.Security.Cryptography;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Application.Interfaces.Services;
using Cerberus.Application.Services;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Keys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cerberus.Infrastructure.Security;

public sealed class KeyManagementOptions
{
    public const string SectionName = "Security:Keys";

    /// <summary>How long a key is used to sign / encrypt new tokens.</summary>
    public TimeSpan ActiveLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How long the next key is published before it becomes active (lets clients refresh their JWKS cache).</summary>
    public TimeSpan PrePublication { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How long a retired key stays published so tokens it signed remain verifiable.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    public int RsaKeySize { get; set; } = 2048;
}

/// <summary>
/// Persistent RSA keys for token signing (RS256, published in JWKS) and token encryption (RSA-OAEP, internal tokens).
/// Private keys are encrypted with ASP.NET Core Data Protection, whose key ring is stored in the database.
/// </summary>
public sealed class SigningKeyManager(
    ISigningKeyRepository repository,
    IUnitOfWork unitOfWork,
    IAuditWriter audit,
    IDataProtectionProvider dataProtectionProvider,
    IClock clock,
    IOptions<KeyManagementOptions> options,
    ILogger<SigningKeyManager> logger)
{
    private const string ProtectorPurpose = "Cerberus.Security.SigningKeys.v1";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    private readonly KeyManagementOptions _options = options.Value;

    /// <summary>Guarantees an active key for each usage and schedules the next one ahead of rotation.</summary>
    public async Task EnsureKeysAsync(CancellationToken cancellationToken = default)
    {
        var created = false;
        foreach (var usage in new[] { KeyUsage.Signing, KeyUsage.Encryption })
        {
            var now = clock.UtcNow;
            var keys = await repository.ListAsync(usage, cancellationToken);
            var active = keys.Where(k => k.GetState(now) == KeyState.Active).MaxBy(k => k.ActivatesAt);
            if (active is null)
            {
                Create(usage, now);
                created = true;
                continue;
            }

            var hasSuccessor = keys.Any(k => k.ActivatesAt >= active.RetiresAt && k.GetState(now) == KeyState.Pending);
            if (!hasSuccessor && active.RetiresAt - now <= _options.PrePublication)
            {
                Create(usage, active.RetiresAt);
                created = true;
            }
        }

        if (created)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Keys to load in the token server, ordered with the key to use first (active), then pending and retired keys
    /// that are only published for verification. Expired keys are excluded.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> LoadKeysAsync(KeyUsage usage, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var keys = await repository.ListAsync(usage, cancellationToken);
        return keys
            .Select(k => new { Key = k, State = k.GetState(now) })
            .Where(x => x.State != KeyState.Expired)
            .OrderBy(x => x.State switch { KeyState.Active => 0, KeyState.Pending => 1, _ => 2 })
            .ThenByDescending(x => x.Key.ActivatesAt)
            .Select(x => (SecurityKey)ToSecurityKey(x.Key))
            .ToList();
    }

    public async Task<IReadOnlyList<(SigningKey Key, KeyState State)>> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var signing = await repository.ListAsync(KeyUsage.Signing, cancellationToken);
        var encryption = await repository.ListAsync(KeyUsage.Encryption, cancellationToken);
        return signing.Concat(encryption).Select(k => (k, k.GetState(now))).ToList();
    }

    private void Create(KeyUsage usage, DateTime activatesAt)
    {
        using var rsa = RSA.Create(_options.RsaKeySize);
        var keyId = Base64UrlEncoder.Encode(new RsaSecurityKey(rsa.ExportParameters(false)).ComputeJwkThumbprint());
        var protectedKey = _protector.Protect(Convert.ToBase64String(rsa.ExportRSAPrivateKey()));
        var algorithm = usage == KeyUsage.Signing ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.RsaOAEP;

        var key = SigningKey.Create(keyId, usage, algorithm, protectedKey, activatesAt, _options.ActiveLifetime, _options.Retention, clock.UtcNow);
        repository.Add(key);
        audit.Write(AuditActions.SigningKeyCreated, targetType: nameof(SigningKey), targetId: keyId,
            details: new { usage = usage.ToString(), activatesAt, key.RetiresAt, key.ExpiresAt });
        logger.LogInformation("Created {Usage} key {KeyId} active from {ActivatesAt:u}.", usage, keyId, activatesAt);
    }

    private RsaSecurityKey ToSecurityKey(SigningKey key)
    {
        var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(Convert.FromBase64String(_protector.Unprotect(key.ProtectedPrivateKey)), out _);
        return new RsaSecurityKey(rsa) { KeyId = key.KeyId };
    }
}
