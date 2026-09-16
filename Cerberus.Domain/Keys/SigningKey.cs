using Cerberus.Domain.Common;
using Cerberus.Domain.Exceptions;

namespace Cerberus.Domain.Keys;

public enum KeyUsage
{
    Signing = 0,
    Encryption = 1
}

public enum KeyState
{
    /// <summary>Published in JWKS but not yet used to sign (lets clients refresh their key cache).</summary>
    Pending = 0,

    /// <summary>Used to sign new tokens and published.</summary>
    Active = 1,

    /// <summary>No longer used to sign, still published so previously issued tokens stay verifiable.</summary>
    Retired = 2,

    /// <summary>Removed from JWKS; kept only for traceability.</summary>
    Expired = 3
}

/// <summary>
/// Asymmetric key used by the token server. The private material is stored encrypted
/// (<see cref="ProtectedPrivateKey"/>, encrypted by the infrastructure); the domain never handles raw key bytes.
/// Invariants: ActivatesAt &lt; RetiresAt &lt;= ExpiresAt; the key id is unique.
/// </summary>
public sealed class SigningKey : Entity
{
    public string KeyId { get; private set; } = string.Empty;
    public KeyUsage Usage { get; private set; }
    public string Algorithm { get; private set; } = string.Empty;
    public string ProtectedPrivateKey { get; private set; } = string.Empty;
    public DateTime ActivatesAt { get; private set; }
    public DateTime RetiresAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    private SigningKey()
    {
    }

    private SigningKey(DateTime utcNow) : base(utcNow)
    {
    }

    public static SigningKey Create(
        string keyId,
        KeyUsage usage,
        string algorithm,
        string protectedPrivateKey,
        DateTime activatesAt,
        TimeSpan activeLifetime,
        TimeSpan retention,
        DateTime utcNow)
    {
        if (activeLifetime <= TimeSpan.Zero || retention < TimeSpan.Zero)
        {
            throw new DomainValidationException("Key lifetimes are inconsistent.", nameof(RetiresAt));
        }

        var retiresAt = activatesAt.Add(activeLifetime);
        return new SigningKey(utcNow)
        {
            KeyId = Check.Required(keyId, nameof(KeyId), 100),
            Usage = usage,
            Algorithm = Check.Required(algorithm, nameof(Algorithm), 20),
            ProtectedPrivateKey = Check.Required(protectedPrivateKey, nameof(ProtectedPrivateKey), 20000),
            ActivatesAt = activatesAt,
            RetiresAt = retiresAt,
            ExpiresAt = retiresAt.Add(retention)
        };
    }

    public KeyState GetState(DateTime utcNow)
    {
        if (utcNow >= ExpiresAt)
        {
            return KeyState.Expired;
        }

        if (utcNow >= RetiresAt)
        {
            return KeyState.Retired;
        }

        return utcNow >= ActivatesAt ? KeyState.Active : KeyState.Pending;
    }

    /// <summary>Immediately withdraws a compromised key: it is neither used nor published anymore.</summary>
    public void Revoke(DateTime utcNow)
    {
        if (ActivatesAt > utcNow)
        {
            ActivatesAt = utcNow;
        }

        RetiresAt = utcNow;
        ExpiresAt = utcNow;
        Touch(utcNow);
    }
}
