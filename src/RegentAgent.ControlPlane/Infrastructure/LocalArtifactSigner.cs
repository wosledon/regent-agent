using System.Security.Cryptography;
using System.Text;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class LocalArtifactSigner
{
    private readonly RSA rsa;

    public LocalArtifactSigner(string keyPath)
    {
        rsa = LoadOrCreate(keyPath);
    }

    public string Sign(string payload) =>
        Convert.ToBase64String(rsa.SignHash(Digest(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    public bool Verify(string payload, string signature)
    {
        try
        {
            return rsa.VerifyHash(Digest(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (FormatException) { return false; }
    }

    public static string ComputeDigest(string payload) => Convert.ToHexString(Digest(payload)).ToLowerInvariant();

    private static byte[] Digest(string payload) => SHA256.HashData(Encoding.UTF8.GetBytes(payload));

    private static RSA LoadOrCreate(string keyPath)
    {
        var rsa = RSA.Create(2048);
        if (File.Exists(keyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(keyPath));
            return rsa;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        File.WriteAllText(keyPath, rsa.ExportRSAPrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return rsa;
    }
}
