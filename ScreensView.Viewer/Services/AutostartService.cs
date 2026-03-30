using Microsoft.Win32;

namespace ScreensView.Viewer.Services;

internal interface IAutostartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public class AutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreensView";

    private readonly string _command;

    public AutostartService()
        : this(Environment.ProcessPath)
    {
    }

    internal AutostartService(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Unable to determine application path for autostart.");

        _command = $"\"{processPath}\"";
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, _command);
            return;
        }

        using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
