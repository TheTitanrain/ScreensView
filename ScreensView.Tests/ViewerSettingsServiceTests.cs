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

        var json = File.ReadAllText(_settingsFile);
        Assert.Contains("\"ConnectionsFilePath\":", json);
        Assert.Contains("C:\\\\Shared\\\\connections.json", json);
    }

    [Fact]
    public void Save_PersistsConnectionsFilePasswordEncrypted()
    {
        var service = new ViewerSettingsService(_settingsFile);
        var settings = new ViewerSettings();

        SetStringProperty(settings, "ConnectionsFilePasswordEncrypted", "encrypted-password-value");

        service.Save(settings);

        var json = File.ReadAllText(_settingsFile);
        Assert.Contains("\"ConnectionsFilePasswordEncrypted\":", json);
        Assert.Contains("encrypted-password-value", json);
    }

    [Fact]
    public void Load_WhenExternalSourceFieldsAreMissing_ReturnsDefaultValues()
    {
        File.WriteAllText(_settingsFile, """
            {"LaunchAtStartup":true,"RefreshIntervalSeconds":12}
            """);

        var service = new ViewerSettingsService(_settingsFile);
        var settings = service.Load();

        Assert.Equal(string.Empty, GetStringProperty(settings, "ConnectionsFilePath"));
        Assert.Equal(string.Empty, GetStringProperty(settings, "ConnectionsFilePasswordEncrypted"));
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
        Assert.Equal(string.Empty, GetStringProperty(loaded, "ConnectionsFilePath"));
        Assert.Equal(string.Empty, GetStringProperty(loaded, "ConnectionsFilePasswordEncrypted"));
    }

    private static void SetStringProperty(ViewerSettings settings, string propertyName, string value)
    {
        var property = typeof(ViewerSettings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        property!.SetValue(settings, value);
    }

    private static string GetStringProperty(ViewerSettings settings, string propertyName)
    {
        var property = typeof(ViewerSettings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return (string?)property!.GetValue(settings) ?? string.Empty;
    }
}
