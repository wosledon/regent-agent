using System.Security.Cryptography;
using System.Text;

namespace RegentAgent.AgentIdentity;

public interface IAgentIdentityKeyProvider
{
    byte[] GetSigningKey();
}

public sealed class FileAgentIdentityKeyProvider(AgentIdentityOptions options) : IAgentIdentityKeyProvider
{
    private readonly AgentIdentityOptions options = options;
    private readonly string keyPath = ResolveKeyPath(options.KeyPath);
    private readonly SemaphoreSlim gate = new(1, 1);

    public byte[] GetSigningKey()
    {
        if (!string.IsNullOrEmpty(options.SigningKey)) return Encoding.UTF8.GetBytes(options.SigningKey);
        gate.Wait();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            if (File.Exists(keyPath))
            {
                var existing = File.ReadAllBytes(keyPath);
                if (existing.Length < 32) throw new InvalidOperationException("Agent token key file is invalid.");
                return existing;
            }
            var generated = RandomNumberGenerator.GetBytes(32);
            using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            stream.Write(generated);
            return generated;
        }
        finally { gate.Release(); }
    }

    private static string ResolveKeyPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath)) return Path.GetFullPath(configuredPath);
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RegentAgent.slnx")))
                return Path.GetFullPath(Path.Combine(directory.FullName, configuredPath));
            directory = directory.Parent;
        }
        return Path.GetFullPath(configuredPath);
    }
}
