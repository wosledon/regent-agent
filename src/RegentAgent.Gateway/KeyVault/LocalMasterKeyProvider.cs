using System.Security.Cryptography;

namespace RegentAgent.Gateway.KeyVault;

public sealed class LocalMasterKeyProvider : IMasterKeyProvider
{
    private readonly string path;
    private readonly byte[]? configuredKey;

    public LocalMasterKeyProvider(string path, string? configuredKey)
    {
        this.path = path;
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            this.configuredKey = Convert.FromBase64String(configuredKey);
            if (this.configuredKey.Length != 32) throw new InvalidOperationException("REGENT_MASTER_KEY must be a 32-byte base64 key.");
        }
    }

    public byte[] GetMasterKey()
    {
        if (configuredKey is not null) return configuredKey;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var key = File.ReadAllBytes(path);
            if (key.Length != 32) throw new InvalidOperationException("Invalid local master key.");
            return key;
        }
        var generated = RandomNumberGenerator.GetBytes(32);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            stream.Write(generated);
        }
        finally
        {
            stream.Dispose();
        }
        return generated;
    }
}
