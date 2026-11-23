using RegentAgent.ControlPlane.Application;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public async Task Locale_timezone_and_currency_are_versioned_and_validated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"localization-{Guid.NewGuid():N}.json");
        try
        {
            var service = new LocalizationService(new JsonDocumentStore<LocalizationSettings>(path));
            var initial = await service.GetAsync("tenant-a", CancellationToken.None);
            Assert.Equal("zh-CN", initial.Locale);
            Assert.Equal("Asia/Shanghai", initial.TimeZone);

            var updated = await service.UpdateAsync("tenant-a",
                new LocalizationSettings("en-US", "UTC", "USD", 1), CancellationToken.None);
            Assert.Equal(2, updated.Version);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync("tenant-a",
                new LocalizationSettings("fr-FR", "UTC", "USD", 2), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync("tenant-a",
                new LocalizationSettings("en-US", "Not/AZone", "USD", 2), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync("tenant-a",
                new LocalizationSettings("en-US", "UTC", "usd", 2), CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
