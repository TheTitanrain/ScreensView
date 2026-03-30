using System.Reflection;
using System.Runtime.ExceptionServices;
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

        var loaded = InvokeLoad(CreateService("correct-password"));

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
                ApiKey = "super-secret-api-key",
                CertThumbprint = "THUMBPRINT-SHOULD-NOT-LEAK"
            }
        };

        var service = CreateService("correct-password");
        InvokeSave(service, computers);

        var rawJson = File.ReadAllText(_filePath);

        Assert.DoesNotContain("Secret Office", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.100.10", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-api-key", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("THUMBPRINT-SHOULD-NOT-LEAK", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithWrongPassword_ThrowsPredictableDomainException()
    {
        var service = CreateService("password-one");
        InvokeSave(service, [new ComputerConfig { Name = "PC", Host = "host", ApiKey = "key" }]);

        var wrongPasswordService = CreateService("password-two");
        var exception = Assert.ThrowsAny<Exception>(() => InvokeLoad(wrongPasswordService));

        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_WritesVersionedEncryptedContainerMetadata()
    {
        var service = CreateService("correct-password");

        InvokeSave(service, [new ComputerConfig { Name = "PC", Host = "host", ApiKey = "key" }]);

        using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
        var root = document.RootElement;
        var properties = root.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(5, properties.Count);
        Assert.Contains("Version", properties.Keys);
        Assert.Contains("KdfSalt", properties.Keys);
        Assert.Contains("Nonce", properties.Keys);
        Assert.Contains("Tag", properties.Keys);
        Assert.Contains("Ciphertext", properties.Keys);

        AssertValidVersionProperty(properties["Version"]);
        AssertValidStringProperty(properties["KdfSalt"]);
        AssertValidStringProperty(properties["Nonce"]);
        AssertValidStringProperty(properties["Tag"]);
        AssertValidStringProperty(properties["Ciphertext"]);
    }

    private object CreateService(string password)
    {
        var type = Type.GetType("ScreensView.Viewer.Services.EncryptedComputerStorageService, ScreensView.Viewer", throwOnError: false);
        Assert.NotNull(type);

        return Activator.CreateInstance(type!, _filePath, password)!;
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

        try
        {
            return (List<ComputerConfig>)load!.Invoke(service, [])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
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

    private static void AssertValidVersionProperty(JsonElement value)
    {
        Assert.True(value.ValueKind is JsonValueKind.Number or JsonValueKind.String);
        if (value.ValueKind == JsonValueKind.String)
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
    }

    private static void AssertValidStringProperty(JsonElement value)
    {
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
    }
}
