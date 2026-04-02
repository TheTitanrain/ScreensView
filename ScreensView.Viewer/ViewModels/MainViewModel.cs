using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int DefaultRefreshIntervalSeconds = 5;
    private const int MinRefreshIntervalSeconds = 1;
    private const int MaxRefreshIntervalSeconds = 60;

    private IComputerStorageService _storage;
    private readonly IScreenshotPollerService _poller;
    private readonly IViewerSettingsService _viewerSettingsService;
    private readonly IAutostartService _autostartService;
    private readonly Action<string, string>? _reportError;

    private ViewerSettings _viewerSettings = new();
    private bool _isSynchronizingAutostart;
    private readonly ILlmCheckService _llmCheckService;
    private readonly IModelDownloadService _downloadService;
    private readonly CancellationTokenSource _appCts = new();

    public ObservableCollection<ComputerViewModel> Computers { get; } = [];

    [ObservableProperty] private int _refreshInterval = DefaultRefreshIntervalSeconds;
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private bool _isAutostartEnabled;
    [ObservableProperty] private int _llmCheckIntervalMinutes = 5;
    [ObservableProperty] private double _modelDownloadProgress = -1;

    public CancellationToken AppToken => _appCts.Token;

    public MainViewModel(IComputerStorageService storage, IScreenshotPollerService poller)
        : this(storage, poller, new ViewerSettingsService(), new AutostartService())
    {
    }

    internal MainViewModel(
        IComputerStorageService storage,
        IScreenshotPollerService poller,
        IViewerSettingsService viewerSettingsService,
        IAutostartService autostartService,
        Action<string, string>? reportError = null,
        ILlmCheckService? llmCheckService = null,
        IModelDownloadService? downloadService = null)
    {
        _storage = storage;
        _poller = poller;
        _viewerSettingsService = viewerSettingsService;
        _autostartService = autostartService;
        _reportError = reportError;

        foreach (var config in _storage.Load())
            Computers.Add(new ComputerViewModel(config));

        _viewerSettings = _viewerSettingsService.Load();
        _refreshInterval = NormalizeRefreshInterval(_viewerSettings.RefreshIntervalSeconds);
        _viewerSettings.RefreshIntervalSeconds = _refreshInterval;
        InitializeAutostartState();

        _poller.Start(Computers, _refreshInterval);
        _isPolling = true;

        _llmCheckService = llmCheckService ?? new LlmCheckService(
            new LlmInferenceService(new ModelDownloadService()));
        _downloadService = downloadService ?? new ModelDownloadService();

        _llmCheckIntervalMinutes = NormalizeLlmCheckInterval(
            _viewerSettings.LlmCheckIntervalMinutes);
        _viewerSettings.LlmCheckIntervalMinutes = _llmCheckIntervalMinutes;

        _downloadService.ModelReady += (_, _) =>
            _llmCheckService.Start(Computers, _llmCheckIntervalMinutes);

        if (_downloadService.IsModelReady)
            _llmCheckService.Start(Computers, _llmCheckIntervalMinutes);
    }

    [RelayCommand]
    private void TogglePolling()
    {
        if (IsPolling)
        {
            _poller.Stop();
            IsPolling = false;
        }
        else
        {
            _poller.Start(Computers, RefreshInterval);
            IsPolling = true;
        }
    }

    [RelayCommand]
    private Task RefreshNowAsync()
    {
        return _poller.RefreshNowAsync(Computers);
    }

    partial void OnRefreshIntervalChanged(int value)
    {
        var normalizedValue = NormalizeRefreshInterval(value);
        if (value != normalizedValue)
        {
            RefreshInterval = normalizedValue;
            return;
        }

        _viewerSettings.RefreshIntervalSeconds = value;
        _viewerSettingsService.Save(_viewerSettings);

        if (IsPolling)
        {
            _poller.Stop();
            _poller.Start(Computers, value);
        }
    }

    partial void OnLlmCheckIntervalMinutesChanged(int value)
    {
        var normalized = NormalizeLlmCheckInterval(value);
        if (value != normalized)
        {
            LlmCheckIntervalMinutes = normalized;
            return;
        }

        _viewerSettings.LlmCheckIntervalMinutes = value;
        _viewerSettingsService.Save(_viewerSettings);

        _llmCheckService.UpdateInterval(value);
    }

    partial void OnIsAutostartEnabledChanged(bool value)
    {
        if (_isSynchronizingAutostart)
            return;

        try
        {
            _autostartService.SetEnabled(value);
            _viewerSettings.LaunchAtStartup = value;
            _viewerSettingsService.Save(_viewerSettings);
        }
        catch (Exception ex)
        {
            SetAutostartState(!value);
            ReportError("Автозапуск", $"Не удалось изменить автозапуск: {ex.Message}");
        }
    }

    public void AddComputer(ComputerConfig config)
    {
        Computers.Add(new ComputerViewModel(config));
        SaveComputers();
    }

    public void AddComputers(IEnumerable<ComputerConfig> configs)
    {
        foreach (var config in configs)
            Computers.Add(new ComputerViewModel(config));
        SaveComputers();
    }

    public void UpdateComputer(ComputerViewModel vm, ComputerConfig config)
    {
        vm.Name = config.Name;
        vm.Host = config.Host;
        vm.Port = config.Port;
        vm.ApiKey = config.ApiKey;
        vm.IsEnabled = config.IsEnabled;
        vm.CertThumbprint = config.CertThumbprint;
        vm.Description = config.Description;
        vm.LastLlmCheck = null;
        SaveComputers();
    }

    public void SetComputerEnabled(ComputerViewModel vm, bool isEnabled)
    {
        if (vm.IsEnabled == isEnabled)
            return;

        var previousValue = vm.IsEnabled;
        vm.IsEnabled = isEnabled;

        try
        {
            SaveComputers();
        }
        catch (Exception ex)
        {
            vm.IsEnabled = previousValue;
            ReportError("Управление компьютерами", $"Не удалось изменить состояние компьютера '{vm.Name}': {ex.Message}");
        }
    }

    public void RemoveComputer(ComputerViewModel vm)
    {
        Computers.Remove(vm);
        SaveComputers();
    }

    public void RemoveComputers(IEnumerable<ComputerViewModel> vms)
    {
        foreach (var vm in vms.ToList())
            Computers.Remove(vm);
        SaveComputers();
    }

    public virtual void SaveComputers()
    {
        _storage.Save(Computers.Select(c => c.ToConfig()));
    }

    internal void ApplyConnectionsSourceChange(
        bool succeeded,
        IComputerStorageService storage,
        IReadOnlyList<ComputerConfig> computers)
    {
        if (!succeeded)
            return;

        var restartPolling = IsPolling;
        if (restartPolling)
            _poller.Stop();

        _storage = storage;
        Computers.Clear();
        foreach (var computer in computers)
            Computers.Add(new ComputerViewModel(computer));

        if (restartPolling)
            _poller.Start(Computers, RefreshInterval);
    }

    public void Dispose()
    {
        _poller.Dispose();
        _llmCheckService.Stop();
        _appCts.Cancel();
        _appCts.Dispose();
    }

    private void InitializeAutostartState()
    {
        try
        {
            var actualState = _autostartService.IsEnabled();
            SetAutostartState(actualState);

            if (_viewerSettings.LaunchAtStartup != actualState)
            {
                _viewerSettings.LaunchAtStartup = actualState;
                _viewerSettingsService.Save(_viewerSettings);
            }
        }
        catch (Exception ex)
        {
            SetAutostartState(_viewerSettings.LaunchAtStartup);
            ReportError("Автозапуск", $"Не удалось прочитать автозапуск: {ex.Message}");
        }
    }

    private void ReportError(string title, string message)
    {
        _reportError?.Invoke(title, message);
    }

    private void SetAutostartState(bool value)
    {
        _isSynchronizingAutostart = true;
        IsAutostartEnabled = value;
        _isSynchronizingAutostart = false;
    }

    private static int NormalizeRefreshInterval(int value)
    {
        return value is >= MinRefreshIntervalSeconds and <= MaxRefreshIntervalSeconds
            ? value
            : DefaultRefreshIntervalSeconds;
    }

    private const int MinLlmCheckIntervalMinutes = 1;
    private const int MaxLlmCheckIntervalMinutes = 60;
    private const int DefaultLlmCheckIntervalMinutes = 5;

    private static int NormalizeLlmCheckInterval(int value) =>
        value is >= MinLlmCheckIntervalMinutes and <= MaxLlmCheckIntervalMinutes
            ? value
            : DefaultLlmCheckIntervalMinutes;

    internal void ReportDownloadError(string message) =>
        ReportError("Model download", message);
}
