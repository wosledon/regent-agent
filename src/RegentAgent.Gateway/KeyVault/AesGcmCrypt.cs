using System.Security.Cryptography;
using System.Text;

namespace RegentAgent.Gateway.KeyVault;

internal static class AesGcmCrypt
{
    public static (string Cipher, string Nonce, string Tag) Encrypt(byte[] key, string value) =>
        EncryptBytes(key, Encoding.UTF8.GetBytes(value));

    public static (string Cipher, string Nonce, string Tag) EncryptBytes(byte[] key, byte[] value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[value.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, value, cipher, tag);
        return (Convert.ToBase64String(cipher), Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
    }

    public static byte[] Unwrap(byte[] key, string cipher, string nonce, string tag)
    {
        var plain = new byte[Convert.FromBase64String(cipher).Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(Convert.FromBase64String(nonce), Convert.FromBase64String(cipher), Convert.FromBase64String(tag), plain);
        return plain;
    }

    public static string Unlock(byte[] key, string cipher, string nonce, string tag) =>
        Encoding.UTF8.GetString(Unwrap(key, cipher, nonce, tag));
}
