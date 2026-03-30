using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    private MainViewModel CreateVm()
    {
        var storage = new ComputerStorageService(_tempFile);
        var poller = new ScreenshotPollerService(new AgentHttpClient());
        return new MainViewModel(storage, poller);
    }

    private MainViewModel CreateVm(
        IViewerSettingsService settingsService,
        IAutostartService autostartService,
        Action<string>? reportError = null)
    {
        var storage = new ComputerStorageService(_tempFile);
        var poller = new ScreenshotPollerService(new AgentHttpClient());
        return new MainViewModel(storage, poller, settingsService, autostartService, reportError);
    }

    [Fact]
    public void Constructor_LoadsAutostartStateFromSystemAndPersistsSyncedValue()
    {
        var settings = new FakeViewerSettingsService(initialValue: false);
        var autostart = new FakeAutostartService(initialValue: true);

        using var vm = CreateVm(settings, autostart);

        Assert.True(vm.IsAutostartEnabled);
        Assert.True(settings.Current.LaunchAtStartup);
        Assert.Equal(1, settings.SaveCalls);
        Assert.Empty(autostart.SetCalls);
    }

    [Fact]
    public void IsAutostartEnabled_WhenEnabled_UpdatesSystemAndPersistsSetting()
    {
        var settings = new FakeViewerSettingsService(initialValue: false);
        var autostart = new FakeAutostartService(initialValue: false);

        using var vm = CreateVm(settings, autostart);
        vm.IsAutostartEnabled = true;

        Assert.True(vm.IsAutostartEnabled);
        Assert.Equal([true], autostart.SetCalls);
        Assert.True(settings.Current.LaunchAtStartup);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void IsAutostartEnabled_WhenSystemUpdateFails_RollsBackAndReportsError()
    {
        var settings = new FakeViewerSettingsService(initialValue: false);
        var autostart = new FakeAutostartService(initialValue: false)
        {
            SetEnabledException = new InvalidOperationException("registry write failed")
        };
        string? error = null;

        using var vm = CreateVm(settings, autostart, message => error = message);
        vm.IsAutostartEnabled = true;

        Assert.False(vm.IsAutostartEnabled);
        Assert.Equal([true], autostart.SetCalls);
        Assert.False(settings.Current.LaunchAtStartup);
        Assert.Equal(0, settings.SaveCalls);
        Assert.Contains("registry write failed", error);
    }

    [Fact]
    public void AddComputers_AddsAllToCollection()
    {
        var vm = CreateVm();
        var configs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
            new ComputerConfig { Name = "PC-3", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
        };

        vm.AddComputers(configs);

        Assert.Equal(3, vm.Computers.Count);
    }

    [Fact]
    public void AddComputers_PersistsAfterReload()
    {
        var storage = new ComputerStorageService(_tempFile);
        var configs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        };

        using (var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient())))
            vm.AddComputers(configs);

        using var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        Assert.Equal(2, vm2.Computers.Count);
    }

    [Fact]
    public void AddComputers_AppendsToExistingCollection()
    {
        var vm = CreateVm();
        vm.AddComputer(new ComputerConfig { Name = "Existing", Host = "10.0.0.100", Port = 5443, ApiKey = "e1" });
        var newConfigs = new[]
        {
            new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        };

        vm.AddComputers(newConfigs);

        Assert.Equal(3, vm.Computers.Count);
    }

    [Fact]
    public void AddComputers_EmptyList_DoesNotThrow()
    {
        var vm = CreateVm();
        var ex = Record.Exception(() => vm.AddComputers([]));
        Assert.Null(ex);
    }

    [Fact]
    public void RemoveComputers_RemovesAllFromCollection()
    {
        var vm = CreateVm();
        vm.AddComputers([
            new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
            new ComputerConfig { Name = "C", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
        ]);
        var toRemove = vm.Computers.Take(2).ToList();

        vm.RemoveComputers(toRemove);

        Assert.Single(vm.Computers);
    }

    [Fact]
    public void RemoveComputers_PersistsAfterReload()
    {
        var storage = new ComputerStorageService(_tempFile);
        using (var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient())))
        {
            vm.AddComputers([
                new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
                new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
                new ComputerConfig { Name = "C", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
            ]);
            vm.RemoveComputers(vm.Computers.Take(2).ToList());
        }

        using var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        Assert.Single(vm2.Computers);
    }

    [Fact]
    public void RemoveComputers_SavesOnce()
    {
        var storage = new ComputerStorageService(_tempFile);
        var vm = new CountingSaveViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        var configs = new[]
        {
            new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
            new ComputerConfig { Name = "C", Host = "10.0.0.3", Port = 5443, ApiKey = "k3" },
        };
        foreach (var c in configs)
            vm.Computers.Add(new ComputerViewModel(c));

        vm.RemoveComputers(vm.Computers.ToList());

        Assert.Equal(1, vm.SaveCount);
    }

    private class FakeViewerSettingsService(bool initialValue) : IViewerSettingsService
    {
        public ViewerSettings Current { get; private set; } = new() { LaunchAtStartup = initialValue };
        public int SaveCalls { get; private set; }

        public ViewerSettings Load() => new() { LaunchAtStartup = Current.LaunchAtStartup };

        public void Save(ViewerSettings settings)
        {
            SaveCalls++;
            Current = new ViewerSettings { LaunchAtStartup = settings.LaunchAtStartup };
        }
    }

    private class FakeAutostartService(bool initialValue) : IAutostartService
    {
        public bool CurrentValue { get; private set; } = initialValue;
        public List<bool> SetCalls { get; } = [];
        public Exception? SetEnabledException { get; set; }

        public bool IsEnabled() => CurrentValue;

        public void SetEnabled(bool enabled)
        {
            SetCalls.Add(enabled);
            if (SetEnabledException is not null)
                throw SetEnabledException;

            CurrentValue = enabled;
        }
    }

    private class CountingSaveViewModel(ComputerStorageService s, ScreenshotPollerService p)
        : MainViewModel(s, p)
    {
        public int SaveCount { get; private set; }
        public override void SaveComputers() => SaveCount++;
    }
}
