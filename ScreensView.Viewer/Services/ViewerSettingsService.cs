using System.IO;
using System.Text.Json;

namespace ScreensView.Viewer.Services;

internal interface IViewerSettingsService
{
    ViewerSettings Load();
    void Save(ViewerSettings settings);
}

public class ViewerSettingsService : IViewerSettingsService
{
    private readonly string _filePath;
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ViewerSettingsService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreensView");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "viewer-settings.json");
    }

    internal ViewerSettingsService(string filePath)
    {
        _filePath = filePath;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public ViewerSettings Load()
    {
        lock (_fileLock)
        {
            if (!File.Exists(_filePath))
                return new ViewerSettings();

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new ViewerSettings();

            return JsonSerializer.Deserialize<ViewerSettings>(json) ?? new ViewerSettings();
        }
    }

    public void Save(ViewerSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        lock (_fileLock)
            File.WriteAllText(_filePath, json);
    }
}

public class ViewerSettings
{
    public bool LaunchAtStartup { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 5;
}
