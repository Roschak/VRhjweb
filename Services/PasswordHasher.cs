using System.Security.Cryptography;

namespace HajjVR.Services;

/// <summary>PBKDF2 password hashing tanpa dependensi eksternal. Format: {iterations}.{salt}.{hash} (base64).</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3) return false;
        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var key = Convert.FromBase64String(parts[2]);
        var attempt = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, key.Length);
        return CryptographicOperations.FixedTimeEquals(attempt, key);
    }
}
