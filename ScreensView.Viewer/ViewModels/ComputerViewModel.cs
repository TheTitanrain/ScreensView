using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.ViewModels;

public enum ComputerStatus { Unknown, Online, Offline, Error, Locked, Disabled }

public partial class ComputerViewModel : ObservableObject
{
    private const string DisabledMessage = "Компьютер отключён в Управлении компьютерами.";

    public Guid Id { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _apiKey;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _certThumbprint = string.Empty;
    [ObservableProperty] private BitmapImage? _screenshot;
    [ObservableProperty] private ComputerStatus _status = ComputerStatus.Unknown;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private DateTime? _lastUpdated;

    public ComputerViewModel(ComputerConfig config)
    {
        Id = config.Id;
        _name = config.Name;
        _host = config.Host;
        _port = config.Port;
        _apiKey = config.ApiKey;
        _isEnabled = config.IsEnabled;
        _certThumbprint = config.CertThumbprint;
        ApplyEnabledState(_isEnabled);
    }

    public ComputerConfig ToConfig() => new()
    {
        Id = Id,
        Name = Name,
        Host = Host,
        Port = Port,
        ApiKey = ApiKey,
        IsEnabled = IsEnabled,
        CertThumbprint = CertThumbprint
    };

    public void UpdateScreenshot(ScreenshotResponse response)
    {
        if (!IsEnabled)
            return;

        var bytes = Convert.FromBase64String(response.ImageBase64);
        var image = new BitmapImage();
        using var ms = new System.IO.MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        Screenshot = image;
        LastUpdated = response.Timestamp;
        Status = ComputerStatus.Online;
        StatusMessage = string.Empty;
    }

    public void SetError(string message)
    {
        if (!IsEnabled)
            return;

        Status = ComputerStatus.Offline;
        StatusMessage = message;
    }

    public void SetLocked(string message)
    {
        if (!IsEnabled)
            return;

        Status = ComputerStatus.Locked;
        StatusMessage = message;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        ApplyEnabledState(value);
    }

    private void ApplyEnabledState(bool isEnabled)
    {
        if (!isEnabled)
        {
            Status = ComputerStatus.Disabled;
            StatusMessage = DisabledMessage;
            return;
        }

        if (Status == ComputerStatus.Disabled)
        {
            Status = ComputerStatus.Unknown;
            StatusMessage = string.Empty;
        }
    }
}
