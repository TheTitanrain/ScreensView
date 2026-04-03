using System.Reflection;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class ViewerSettingsServiceTests : IDisposable
{
    private readonly string _settingsFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_settingsFile))
            File.Delete(_settingsFile);
    }

    [Fact]
    public void Save_PersistsConnectionsFilePath()
    {
        var service = new ViewerSettingsService(_settingsFile);
        var settings = new ViewerSettings();

        SetStringProperty(settings, "ConnectionsFilePath", @"C:\Shared\connections.json");

        service.Save(settings);

        var loaded = service.Load();
        Assert.Equal(@"C:\Shared\connections.json", GetRequiredStringProperty(loaded, "ConnectionsFilePath"));
    }

    [Fact]
    public void Save_PersistsConnectionsFilePasswordEncrypted()
    {
        var service = new ViewerSettingsService(_settingsFile);
        var settings = new ViewerSettings();

        SetStringProperty(settings, "ConnectionsFilePasswordEncrypted", "encrypted-password-value");

        service.Save(settings);

        var loaded = service.Load();
        Assert.Equal("encrypted-password-value", GetRequiredStringProperty(loaded, "ConnectionsFilePasswordEncrypted"));
    }

    [Fact]
    public void Load_WhenExternalSourceFieldsAreMissing_ReturnsDefaultValues()
    {
        File.WriteAllText(_settingsFile, """
            {"LaunchAtStartup":true,"RefreshIntervalSeconds":12}
            """);

        var service = new ViewerSettingsService(_settingsFile);
        var settings = service.Load();

        Assert.Equal(string.Empty, GetRequiredStringProperty(settings, "ConnectionsFilePath"));
        Assert.Equal(string.Empty, GetRequiredStringProperty(settings, "ConnectionsFilePasswordEncrypted"));
    }

    [Fact]
    public void Load_WhenExternalSourceFieldsAreExplicitNulls_NormalizesToDefaultStrings()
    {
        File.WriteAllText(_settingsFile, """
            {"ConnectionsFilePath":null,"ConnectionsFilePasswordEncrypted":null}
            """);

        var service = new ViewerSettingsService(_settingsFile);
        var settings = service.Load();

        Assert.Equal(string.Empty, GetRequiredStringProperty(settings, "ConnectionsFilePath"));
        Assert.Equal(string.Empty, GetRequiredStringProperty(settings, "ConnectionsFilePasswordEncrypted"));
    }

    [Fact]
    public void Save_WhenExternalSourceFieldsAreCleared_RoundTripsEmptyStrings()
    {
        var service = new ViewerSettingsService(_settingsFile);
        var settings = new ViewerSettings();

        SetStringProperty(settings, "ConnectionsFilePath", @"C:\Shared\connections.json");
        SetStringProperty(settings, "ConnectionsFilePasswordEncrypted", "encrypted-password-value");

        service.Save(settings);

        SetStringProperty(settings, "ConnectionsFilePath", string.Empty);
        SetStringProperty(settings, "ConnectionsFilePasswordEncrypted", string.Empty);
        service.Save(settings);

        var loaded = service.Load();
        Assert.Equal(string.Empty, GetRequiredStringProperty(loaded, "ConnectionsFilePath"));
        Assert.Equal(string.Empty, GetRequiredStringProperty(loaded, "ConnectionsFilePasswordEncrypted"));
    }

    private static void SetStringProperty(ViewerSettings settings, string propertyName, string value)
    {
        var property = typeof(ViewerSettings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        property!.SetValue(settings, value);
    }

    private static string GetRequiredStringProperty(ViewerSettings settings, string propertyName)
    {
        var property = typeof(ViewerSettings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        var value = property!.GetValue(settings);
        Assert.IsType<string>(value);
        return (string)value;
    }

    [Fact]
    public void LlmCheckIntervalMinutes_DefaultValue_IsFive()
    {
        var settings = new ViewerSettings();
        Assert.Equal(5, settings.LlmCheckIntervalMinutes);
    }

    [Fact]
    public void LlmCheckIntervalMinutes_PersistsAcrossSaveLoad()
    {
        var path = Path.GetTempFileName();
        try
        {
            var svc = new ViewerSettingsService(path);
            var settings = svc.Load();
            settings.LlmCheckIntervalMinutes = 15;
            svc.Save(settings);

            var loaded = svc.Load();
            Assert.Equal(15, loaded.LlmCheckIntervalMinutes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LlmEnabled_DefaultValue_IsFalse()
    {
        var settings = new ViewerSettings();
        Assert.False(settings.LlmEnabled);
    }

    [Fact]
    public void LlmEnabled_PersistsAcrossSaveLoad()
    {
        var path = Path.GetTempFileName();
        try
        {
            var svc = new ViewerSettingsService(path);
            var settings = svc.Load();
            settings.LlmEnabled = true;
            svc.Save(settings);

            var loaded = svc.Load();
            Assert.True(loaded.LlmEnabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectedModelId_PersistsAcrossSaveLoad()
    {
        var path = Path.GetTempFileName();
        try
        {
            var svc = new ViewerSettingsService(path);
            var settings = svc.Load();
            settings.SelectedModelId = "some-model-id";
            svc.Save(settings);

            var loaded = svc.Load();
            Assert.Equal("some-model-id", loaded.SelectedModelId);
        }
        finally { File.Delete(path); }
    }
}
