using System.Security.Cryptography;

namespace TLEStudio.Services;

public static class VerificationTokenUtility
{
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string ComputeSha256Hex(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}