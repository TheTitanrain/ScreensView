using System.Reflection;
using System.Text.Json;
using ScreensView.Shared.Models;

namespace ScreensView.Tests;

public class EncryptedComputerStorageServiceTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".enc.json");

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    [Fact]
    public void SaveAndLoad_WithCorrectPassword_RoundTripsAllComputerConfigs()
    {
        var computers = new List<ComputerConfig>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Office PC",
                Host = "192.168.1.42",
                Port = 5443,
                IsEnabled = true,
                ApiKey = "api-key-one",
                CertThumbprint = "THUMBPRINT-1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Lab PC",
                Host = "10.0.0.7",
                Port = 5444,
                IsEnabled = false,
                ApiKey = "api-key-two",
                CertThumbprint = "THUMBPRINT-2"
            }
        };

        var service = CreateService("correct-password");
        InvokeSave(service, computers);

        var loaded = InvokeLoad(service);

        Assert.Equal(2, loaded.Count);
        AssertComputerConfigsEqual(computers[0], loaded[0]);
        AssertComputerConfigsEqual(computers[1], loaded[1]);
    }

    [Fact]
    public void Save_WritesOnlyEncryptedContainerWithoutPlaintextHostsOrApiKeys()
    {
        var computers = new List<ComputerConfig>
        {
            new()
            {
                Name = "Secret Office",
                Host = "192.168.100.10",
                ApiKey = "super-secret-api-key"
            }
        };

        var service = CreateService("correct-password");
        InvokeSave(service, computers);

        var rawJson = File.ReadAllText(_filePath);

        Assert.DoesNotContain("Secret Office", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.100.10", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-api-key", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithWrongPassword_ThrowsPredictableDomainException()
    {
        var service = CreateService("password-one");
        InvokeSave(service, [new ComputerConfig { Name = "PC", Host = "host", ApiKey = "key" }]);

        var wrongPasswordService = CreateService("password-two");
        var exception = Assert.ThrowsAny<Exception>(() => InvokeLoad(wrongPasswordService));

        Assert.Equal("ScreensView.Viewer.Services.EncryptedComputerStoragePasswordException", exception.GetType().FullName);
    }

    [Fact]
    public void Save_WritesVersionedEncryptedContainerMetadata()
    {
        var service = CreateService("correct-password");

        InvokeSave(service, [new ComputerConfig { Name = "PC", Host = "host", ApiKey = "key" }]);

        using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("Version", out var version));
        Assert.Equal(1, version.GetInt32());
        Assert.True(root.TryGetProperty("KdfSalt", out var kdfSalt));
        Assert.Equal(JsonValueKind.String, kdfSalt.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(kdfSalt.GetString()));
        Assert.True(root.TryGetProperty("Nonce", out var nonce));
        Assert.Equal(JsonValueKind.String, nonce.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(nonce.GetString()));
        Assert.True(root.TryGetProperty("Tag", out var tag));
        Assert.Equal(JsonValueKind.String, tag.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(tag.GetString()));
        Assert.True(root.TryGetProperty("Ciphertext", out var ciphertext));
        Assert.Equal(JsonValueKind.String, ciphertext.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(ciphertext.GetString()));
    }

    private object CreateService(string password)
    {
        var type = Type.GetType("ScreensView.Viewer.Services.EncryptedComputerStorageService, ScreensView.Viewer", throwOnError: false);
        Assert.NotNull(type);

        var ctor = type!.GetConstructor(new[] { typeof(string), typeof(string) });
        Assert.NotNull(ctor);

        return ctor!.Invoke([_filePath, password]);
    }

    private static void InvokeSave(object service, IEnumerable<ComputerConfig> computers)
    {
        var save = service.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(save);

        save!.Invoke(service, [computers]);
    }

    private static List<ComputerConfig> InvokeLoad(object service)
    {
        var load = service.GetType().GetMethod("Load", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(load);

        return (List<ComputerConfig>)load!.Invoke(service, [])!;
    }

    private static void AssertComputerConfigsEqual(ComputerConfig expected, ComputerConfig actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Host, actual.Host);
        Assert.Equal(expected.Port, actual.Port);
        Assert.Equal(expected.IsEnabled, actual.IsEnabled);
        Assert.Equal(expected.ApiKey, actual.ApiKey);
        Assert.Equal(expected.CertThumbprint, actual.CertThumbprint);
    }
}
