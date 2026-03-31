using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private readonly Action<string>? _reportAutostartError;
    private readonly Dictionary<ComputerViewModel, int> _trackedComputers = new();

    private ViewerSettings _viewerSettings = new();
    private bool _isSynchronizingAutostart;

    public ObservableCollection<ComputerViewModel> Computers { get; } = [];

    [ObservableProperty] private int _refreshInterval = DefaultRefreshIntervalSeconds;
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private bool _isAutostartEnabled;

    public int ActiveComputerCount => Computers.Count(c => c.IsEnabled);
    public int OnlineComputerCount => Computers.Count(c => c.IsEnabled && c.Status == ComputerStatus.Online);
    public int ProblemComputerCount => Computers.Count(c => c.IsEnabled && c.Status is ComputerStatus.Offline or ComputerStatus.Error);
    public string PollingStateText => IsPolling
        ? $"Опрос запущен, интервал {RefreshInterval} сек."
        : "Опрос остановлен.";

    public MainViewModel(IComputerStorageService storage, IScreenshotPollerService poller)
        : this(storage, poller, new ViewerSettingsService(), new AutostartService())
    {
    }

    internal MainViewModel(
        IComputerStorageService storage,
        IScreenshotPollerService poller,
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

        Computers.CollectionChanged += OnComputersCollectionChanged;
        foreach (var computer in Computers)
            SubscribeToComputer(computer);

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

        RaisePollingTextChanged();
    }

    partial void OnIsPollingChanged(bool value)
    {
        RaisePollingTextChanged();
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
        Computers.CollectionChanged -= OnComputersCollectionChanged;

        foreach (var pair in _trackedComputers.ToArray())
        {
            for (var i = 0; i < pair.Value; i++)
                UnsubscribeFromComputer(pair.Key);
        }

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

    private void OnComputersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var computer in _trackedComputers.Keys.ToArray())
                UnsubscribeFromComputer(computer);
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (ComputerViewModel computer in e.OldItems)
                    UnsubscribeFromComputer(computer);
            }

            if (e.NewItems is not null)
            {
                foreach (ComputerViewModel computer in e.NewItems)
                    SubscribeToComputer(computer);
            }
        }

        RaiseComputerSummaryCounts();
    }

    private void OnComputerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            RaiseComputerSummaryCounts();
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ComputerViewModel.IsEnabled):
                OnPropertyChanged(nameof(ActiveComputerCount));
                OnPropertyChanged(nameof(OnlineComputerCount));
                OnPropertyChanged(nameof(ProblemComputerCount));
                break;
            case nameof(ComputerViewModel.Status):
                OnPropertyChanged(nameof(OnlineComputerCount));
                OnPropertyChanged(nameof(ProblemComputerCount));
                break;
        }
    }

    private void SubscribeToComputer(ComputerViewModel computer)
    {
        if (_trackedComputers.TryGetValue(computer, out var count))
        {
            _trackedComputers[computer] = count + 1;
            return;
        }

        _trackedComputers[computer] = 1;
        computer.PropertyChanged += OnComputerPropertyChanged;
    }

    private void UnsubscribeFromComputer(ComputerViewModel computer)
    {
        if (!_trackedComputers.TryGetValue(computer, out var count))
            return;

        if (count > 1)
        {
            _trackedComputers[computer] = count - 1;
            return;
        }

        _trackedComputers.Remove(computer);
        computer.PropertyChanged -= OnComputerPropertyChanged;
    }

    private void RaiseComputerSummaryCounts()
    {
        OnPropertyChanged(nameof(ActiveComputerCount));
        OnPropertyChanged(nameof(OnlineComputerCount));
        OnPropertyChanged(nameof(ProblemComputerCount));
    }

    private void RaisePollingTextChanged()
    {
        OnPropertyChanged(nameof(PollingStateText));
    }

    private static int NormalizeRefreshInterval(int value)
    {
        return value is >= MinRefreshIntervalSeconds and <= MaxRefreshIntervalSeconds
            ? value
            : DefaultRefreshIntervalSeconds;
    }
}
