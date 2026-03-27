using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ComputerStorageService _storage;
    private readonly ScreenshotPollerService _poller;

    public ObservableCollection<ComputerViewModel> Computers { get; } = [];

    [ObservableProperty] private int _refreshInterval = 5;
    [ObservableProperty] private bool _isPolling;

    public MainViewModel(ComputerStorageService storage, ScreenshotPollerService poller)
    {
        _storage = storage;
        _poller = poller;

        foreach (var config in _storage.Load())
            Computers.Add(new ComputerViewModel(config));
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
        if (IsPolling)
        {
            _poller.Stop();
            _poller.Start(Computers, value);
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

    public void SaveComputers()
    {
        _storage.Save(Computers.Select(c => c.ToConfig()));
    }

    public void Dispose()
    {
        _poller.Dispose();
    }
}
