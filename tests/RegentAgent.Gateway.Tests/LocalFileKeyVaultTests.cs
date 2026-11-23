using RegentAgent.Gateway.KeyVault;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class LocalFileKeyVaultTests
{
    [Fact]
    public async Task Secrets_are_encrypted_and_rotation_keeps_key_reference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-vault-{Guid.NewGuid():N}");
        try
        {
            var master = new FixedMasterKeyProvider();
            var vault = new LocalFileKeyVault(Path.Combine(root, "vault.json"), master);
            var created = await vault.SetAsync("tenant-a", "openai", "local-cheap", "super-secret-value", CancellationToken.None);
            Assert.Equal("super-secret-value", await vault.UnlockAsync("tenant-a", created.KeyRef, CancellationToken.None));
            Assert.Null(await vault.UnlockAsync("tenant-b", created.KeyRef, CancellationToken.None));
            Assert.DoesNotContain("super-secret-value", await File.ReadAllTextAsync(Path.Combine(root, "vault.json")));

            var rotated = await vault.RotateAsync(created.KeyRef, "new-secret-value", CancellationToken.None);
            Assert.Equal(created.KeyRef, rotated.KeyRef);
            Assert.Equal(2, rotated.Version);
            Assert.Equal("new-secret-value", await vault.UnlockAsync("tenant-a", created.KeyRef, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class FixedMasterKeyProvider : IMasterKeyProvider
    {
        public byte[] GetMasterKey() => Enumerable.Repeat((byte)7, 32).ToArray();
    }
}
