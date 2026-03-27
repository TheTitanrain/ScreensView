using System.IO;
using System.Text.Json;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Helpers;

namespace ScreensView.Viewer.Services;

public class ComputerStorageService
{
    private readonly string _filePath;
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ComputerStorageService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreensView");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "computers.json");
    }

    internal ComputerStorageService(string filePath)
    {
        _filePath = filePath;
    }

    public List<ComputerConfig> Load()
    {
        lock (_fileLock)
        {
            if (!File.Exists(_filePath))
                return [];

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrEmpty(json))
                return [];
            var items = JsonSerializer.Deserialize<List<StoredComputer>>(json) ?? [];

            return items.Select(s => new ComputerConfig
            {
                Id = s.Id,
                Name = s.Name,
                Host = s.Host,
                Port = s.Port,
                IsEnabled = s.IsEnabled,
                ApiKey = TryDecrypt(s.ApiKeyEncrypted)
            }).ToList();
        }
    }

    public void Save(IEnumerable<ComputerConfig> computers)
    {
        var items = computers.Select(c => new StoredComputer
        {
            Id = c.Id,
            Name = c.Name,
            Host = c.Host,
            Port = c.Port,
            IsEnabled = c.IsEnabled,
            ApiKeyEncrypted = string.IsNullOrEmpty(c.ApiKey) ? string.Empty : DpapiHelper.Encrypt(c.ApiKey)
        }).ToList();

        var json = JsonSerializer.Serialize(items, JsonOptions);
        lock (_fileLock)
            File.WriteAllText(_filePath, json);
    }

    private static string TryDecrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        try { return DpapiHelper.Decrypt(encrypted); }
        catch { return string.Empty; }
    }

    private class StoredComputer
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool IsEnabled { get; set; }
        public string ApiKeyEncrypted { get; set; } = string.Empty;
    }
}
