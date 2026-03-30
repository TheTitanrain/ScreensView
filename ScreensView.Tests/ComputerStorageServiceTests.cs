using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class LocalConnectionsStorageTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    private ComputerStorageService CreateService() => new(_tempFile);

    [Fact]
    public void Load_FileDoesNotExist_ReturnsEmpty()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var service = new ComputerStorageService(nonExistentPath);

        var result = service.Load();

        Assert.Empty(result);
    }

    [Fact]
    public void SaveAndLoad_PreservesPersistedFields()
    {
        var original = new ComputerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Office PC",
            Host = "192.168.1.42",
            Port = 5443,
            IsEnabled = true,
            ApiKey = "super-secret-key-xyz",
            CertThumbprint = "AABBCCDDEEFF00112233445566778899AABBCCDD"
        };
        var service = CreateService();

        service.Save([original]);
        var loaded = service.Load();

        Assert.Single(loaded);
        var c = loaded[0];
        Assert.Equal(original.Id, c.Id);
        Assert.Equal(original.Name, c.Name);
        Assert.Equal(original.Host, c.Host);
        Assert.Equal(original.Port, c.Port);
        Assert.Equal(original.IsEnabled, c.IsEnabled);
        Assert.Equal(original.ApiKey, c.ApiKey);
        Assert.Equal(original.CertThumbprint, c.CertThumbprint);
    }

    [Fact]
    public void SaveAndLoad_EmptyApiKey_RemainsEmpty()
    {
        var computer = new ComputerConfig { Id = Guid.NewGuid(), ApiKey = string.Empty };
        var service = CreateService();

        service.Save([computer]);
        var loaded = service.Load();

        Assert.Equal(string.Empty, loaded[0].ApiKey);
    }

    [Fact]
    public void SaveAndLoad_MultipleComputers_AllPreserved()
    {
        var computers = Enumerable.Range(1, 3).Select(i => new ComputerConfig
        {
            Id = Guid.NewGuid(),
            Name = $"PC-{i}",
            Host = $"10.0.0.{i}",
            Port = 5440 + i,
            ApiKey = $"key-{i}"
        }).ToList();
        var service = CreateService();

        service.Save(computers);
        var loaded = service.Load();

        Assert.Equal(3, loaded.Count);
        Assert.All(loaded, c => Assert.StartsWith("key-", c.ApiKey));
    }

    [Fact]
    public void Save_OverwritesPreviousData()
    {
        var service = CreateService();
        service.Save([new ComputerConfig { Name = "First" }, new ComputerConfig { Name = "Second" }]);

        service.Save([new ComputerConfig { Name = "Only" }]);
        var loaded = service.Load();

        Assert.Single(loaded);
        Assert.Equal("Only", loaded[0].Name);
    }

    [Fact]
    public void Load_CorruptedEncryptedApiKey_ReturnsEmptyApiKey()
    {
        // Simulate a file where the encrypted key is invalid (e.g., from a different machine)
        var json = """
            [{"Id":"00000000-0000-0000-0000-000000000001","Name":"PC","Host":"host","Port":5443,"IsEnabled":true,"ApiKeyEncrypted":"bm90LXZhbGlkLWRwYXBpLWRhdGE="}]
            """;
        File.WriteAllText(_tempFile, json);

        var loaded = CreateService().Load();

        Assert.Single(loaded);
        Assert.Equal(string.Empty, loaded[0].ApiKey);
    }

    [Fact]
    public void Load_ApiKeyEncryptedIsEmpty_ReturnsEmptyApiKey()
    {
        var json = """
            [{"Id":"00000000-0000-0000-0000-000000000001","Name":"PC","Host":"host","Port":5443,"IsEnabled":false,"ApiKeyEncrypted":""}]
            """;
        File.WriteAllText(_tempFile, json);

        var loaded = CreateService().Load();

        Assert.Equal(string.Empty, loaded[0].ApiKey);
    }

    [Fact]
    public void Save_ApiKeyIsEncryptedOnDisk()
    {
        const string apiKey = "plaintext-api-key";
        var service = CreateService();

        service.Save([new ComputerConfig { ApiKey = apiKey }]);

        var rawJson = File.ReadAllText(_tempFile);
        Assert.DoesNotContain(apiKey, rawJson);
    }

    [Fact]
    public void Save_LocalStorageStillPersistsPlaintextHostAndEncryptedApiKey()
    {
        var service = CreateService();

        service.Save([new ComputerConfig
        {
            Name = "Office PC",
            Host = "192.168.1.42",
            ApiKey = "plaintext-api-key"
        }]);

        var rawJson = File.ReadAllText(_tempFile);
        Assert.Contains("Office PC", rawJson);
        Assert.Contains("192.168.1.42", rawJson);
        Assert.DoesNotContain("plaintext-api-key", rawJson);
        Assert.Contains("ApiKeyEncrypted", rawJson);
    }
}
