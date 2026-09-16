using System.Security.Cryptography;
using System.Text;

namespace Cerberus.Application.Common;

public static class SecureTokens
{
    /// <summary>Generates a URL-safe random token with 256 bits of entropy.</summary>
    public static string Generate(int byteLength = 32)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
