using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.ViewModels;

public enum ComputerStatus { Unknown, Online, Offline, Error, Locked, Disabled }

public enum LlmTileStatus
{
    Inactive,      // service not running
    NoDescription, // service running but description is null/empty
    Waiting,       // has description, waiting for first check or screenshot
    Checking,      // IsLlmChecking = true
    Match,         // LastLlmCheck.IsMatch && !IsError
    Mismatch,      // !LastLlmCheck.IsMatch && !IsError
    Error          // LastLlmCheck.IsError
}

public partial class ComputerViewModel : ObservableObject
{
    private static string DisabledMessage => LocalizationService.Get("Str.Vm.DisabledMessage");

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
    [ObservableProperty] private string? _description;
    [ObservableProperty] private LlmCheckResult? _lastLlmCheck;
    [ObservableProperty] private bool _isLlmChecking;
    [ObservableProperty] private bool _isLlmServiceActive;
    [ObservableProperty] private LlmTileStatus _llmStatus = LlmTileStatus.Inactive;

    /// <summary>Bottom-bar time text: "HH:mm:ss" or localized "Off" when disabled.</summary>
    public string BottomBarTimeText =>
        Status == ComputerStatus.Disabled
            ? LocalizationService.Get("Str.Tile.Disabled")
            : LastUpdated?.ToString("HH:mm:ss") ?? string.Empty;

    /// <summary>Tooltip text for the LLM badge.</summary>
    public string? LlmTooltipText
    {
        get
        {
            if (IsLlmChecking) return LocalizationService.Get("Str.Llm.Analysing");
            if (LastLlmCheck is LlmCheckResult r)
            {
                var prefix = r.IsError ? LocalizationService.Get("Str.Llm.Error") :
                             r.IsMatch ? LocalizationService.Get("Str.Llm.Match")
                                       : LocalizationService.Get("Str.Llm.Mismatch");
                return $"{prefix} — {r.Explanation}";
            }
            return null;
        }
    }

    /// <summary>Called by MainViewModel when the UI language is switched.</summary>
    public void NotifyLanguageChanged()
    {
        if (Status == ComputerStatus.Disabled)
            StatusMessage = LocalizationService.Get("Str.Vm.DisabledMessage");
        OnPropertyChanged(nameof(BottomBarTimeText));
        OnPropertyChanged(nameof(LlmTooltipText));
    }

    public ComputerViewModel(ComputerConfig config)
    {
        Id = config.Id;
        _name = config.Name;
        _host = config.Host;
        _port = config.Port;
        _apiKey = config.ApiKey;
        _isEnabled = config.IsEnabled;
        _certThumbprint = config.CertThumbprint;
        _description = config.Description;
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
        CertThumbprint = CertThumbprint,
        Description = Description
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

        LastLlmCheck = null;
        Status = ComputerStatus.Offline;
        StatusMessage = message;
    }

    public void SetLocked(string message)
    {
        if (!IsEnabled)
            return;

        LastLlmCheck = null;
        Status = ComputerStatus.Locked;
        StatusMessage = message;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        ApplyEnabledState(value);
    }

    partial void OnDescriptionChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
            LastLlmCheck = null;
        UpdateLlmStatus();
    }

    partial void OnStatusChanged(ComputerStatus value) => OnPropertyChanged(nameof(BottomBarTimeText));
    partial void OnLastUpdatedChanged(DateTime? value) => OnPropertyChanged(nameof(BottomBarTimeText));
    partial void OnIsLlmServiceActiveChanged(bool value) => UpdateLlmStatus();
    partial void OnIsLlmCheckingChanged(bool value) { UpdateLlmStatus(); OnPropertyChanged(nameof(LlmTooltipText)); }
    partial void OnLastLlmCheckChanged(LlmCheckResult? value) { UpdateLlmStatus(); OnPropertyChanged(nameof(LlmTooltipText)); }

    private LlmTileStatus ComputeLlmStatus() => _isLlmServiceActive switch
    {
        false => LlmTileStatus.Inactive,
        true when string.IsNullOrEmpty(_description)   => LlmTileStatus.NoDescription,
        true when _isLlmChecking                       => LlmTileStatus.Checking,
        true when _lastLlmCheck is null                => LlmTileStatus.Waiting,
        true when _lastLlmCheck.IsError                => LlmTileStatus.Error,
        true when _lastLlmCheck.IsMatch                => LlmTileStatus.Match,
        _                                              => LlmTileStatus.Mismatch
    };

    private void UpdateLlmStatus() => LlmStatus = ComputeLlmStatus();

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
