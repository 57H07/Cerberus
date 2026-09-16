using System.Text;

namespace Cerberus.Domain.Common;

/// <summary>
/// Single normalization rule for every globally unique identifier (login, email, role name):
/// trim, Unicode NFKC compatibility normalization, then invariant upper case.
/// </summary>
public static class IdentityNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    }
}
