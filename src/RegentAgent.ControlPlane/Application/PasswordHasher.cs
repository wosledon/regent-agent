using System.Security.Cryptography;
using System.Text;
using Argon2 = Isopoh.Cryptography.Argon2.Argon2;
using Argon2Config = Isopoh.Cryptography.Argon2.Argon2Config;
using Argon2Type = Isopoh.Cryptography.Argon2.Argon2Type;
using Argon2Version = Isopoh.Cryptography.Argon2.Argon2Version;

namespace RegentAgent.ControlPlane.Application;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 65536,
            Lanes = 4,
            Threads = 2,
            HashLength = 32,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = RandomNumberGenerator.GetBytes(16)
        };
        return Argon2.Hash(config);
    }

    public static bool Verify(string password, string stored)
    {
        if (stored.StartsWith("$argon2id$", StringComparison.Ordinal))
        {
            try { return Argon2.Verify(stored, password); }
            catch (ArgumentException) { return false; }
        }
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), iterations,
            HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static bool IsLegacy(string stored) => !stored.StartsWith("$argon2id$", StringComparison.Ordinal);
}
