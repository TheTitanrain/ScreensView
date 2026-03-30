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

    private readonly ComputerStorageService _storage;
    private readonly ScreenshotPollerService _poller;
    private readonly IViewerSettingsService _viewerSettingsService;
    private readonly IAutostartService _autostartService;
    private readonly Action<string>? _reportAutostartError;

    private ViewerSettings _viewerSettings = new();
    private bool _isSynchronizingAutostart;

    public ObservableCollection<ComputerViewModel> Computers { get; } = [];

    [ObservableProperty] private int _refreshInterval = DefaultRefreshIntervalSeconds;
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private bool _isAutostartEnabled;

    public MainViewModel(ComputerStorageService storage, ScreenshotPollerService poller)
        : this(storage, poller, new ViewerSettingsService(), new AutostartService())
    {
    }

    internal MainViewModel(
        ComputerStorageService storage,
        ScreenshotPollerService poller,
        IViewerSettingsService viewerSettingsService,
        IAutostartService autostartService,
        Action<string>? reportAutostartError = null)
    {
        _storage = storage;
        _poller = poller;
        _viewerSettingsService = viewerSettingsService;
        _autostartService = autostartService;
        _reportAutostartError = reportAutostartError;

        foreach (var config in _storage.Load())
            Computers.Add(new ComputerViewModel(config));

        _viewerSettings = _viewerSettingsService.Load();
        _refreshInterval = NormalizeRefreshInterval(_viewerSettings.RefreshIntervalSeconds);
        _viewerSettings.RefreshIntervalSeconds = _refreshInterval;
        InitializeAutostartState();
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
            _reportAutostartError?.Invoke($"Не удалось изменить автозапуск: {ex.Message}");
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
        SaveComputers();
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

    public void Dispose()
    {
        _poller.Dispose();
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
            _reportAutostartError?.Invoke($"Не удалось прочитать автозапуск: {ex.Message}");
        }
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
}
