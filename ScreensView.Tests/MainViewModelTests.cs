using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();
    private readonly string _settingsFile = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_tempFile);
        File.Delete(_settingsFile);
    }

    private MainViewModel CreateVm()
    {
        var storage = new ComputerStorageService(_tempFile);
        var poller = new ScreenshotPollerService(new AgentHttpClient());
        return new MainViewModel(storage, poller);
    }

    private MainViewModel CreateVm(
        IViewerSettingsService settingsService,
        IAutostartService autostartService,
        Action<string, string>? reportError = null)
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
        string? title = null;
        string? error = null;

        using var vm = CreateVm(settings, autostart, (reportedTitle, message) =>
        {
            title = reportedTitle;
            error = message;
        });
        vm.IsAutostartEnabled = true;

        Assert.False(vm.IsAutostartEnabled);
        Assert.Equal([true], autostart.SetCalls);
        Assert.False(settings.Current.LaunchAtStartup);
        Assert.Equal(0, settings.SaveCalls);
        Assert.Equal("Автозапуск", title);
        Assert.Contains("registry write failed", error);
    }

    [Fact]
    public void Constructor_LoadsRefreshIntervalFromViewerSettings()
    {
        File.WriteAllText(_settingsFile, "{\"LaunchAtStartup\":false,\"RefreshIntervalSeconds\":12}");
        var settings = new ViewerSettingsService(_settingsFile);
        var autostart = new FakeAutostartService(initialValue: false);

        using var vm = CreateVm(settings, autostart);

        Assert.Equal(12, vm.RefreshInterval);
    }

    [Fact]
    public void RefreshInterval_WhenChanged_PersistsValueToViewerSettings()
    {
        var settings = new ViewerSettingsService(_settingsFile);
        var autostart = new FakeAutostartService(initialValue: false);

        using var vm = CreateVm(settings, autostart);
        vm.RefreshInterval = 9;

        var json = File.ReadAllText(_settingsFile);
        Assert.Contains("\"RefreshIntervalSeconds\": 9", json);
    }

    [Fact]
    public void SetComputerEnabled_WhenDisabling_UpdatesViewModelAndPersists()
    {
        var storage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "PC", Host = "10.0.0.1", ApiKey = "key", IsEnabled = true }
            ]
        };

        using var vm = CreateVm(storage, new FakeScreenshotPollerService());
        var target = vm.Computers[0];

        vm.SetComputerEnabled(target, false);

        Assert.False(target.IsEnabled);
        Assert.Single(storage.SavedSnapshots);
        Assert.False(storage.SavedSnapshots[0][0].IsEnabled);
    }

    [Fact]
    public void SetComputerEnabled_WhenEnabling_UpdatesViewModelAndPersists()
    {
        var storage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "PC", Host = "10.0.0.1", ApiKey = "key", IsEnabled = false }
            ]
        };

        using var vm = CreateVm(storage, new FakeScreenshotPollerService());
        var target = vm.Computers[0];

        vm.SetComputerEnabled(target, true);

        Assert.True(target.IsEnabled);
        Assert.Single(storage.SavedSnapshots);
        Assert.True(storage.SavedSnapshots[0][0].IsEnabled);
    }

    [Fact]
    public void SetComputerEnabled_WhenSaveFails_RevertsStateAndReportsError()
    {
        var storage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "PC", Host = "10.0.0.1", ApiKey = "key", IsEnabled = true }
            ],
            SaveException = new InvalidOperationException("disk full")
        };
        string? title = null;
        string? error = null;

        using var vm = new MainViewModel(
            storage,
            new FakeScreenshotPollerService(),
            new FakeViewerSettingsService(initialValue: false),
            new FakeAutostartService(initialValue: false),
            (reportedTitle, message) =>
            {
                title = reportedTitle;
                error = message;
            });
        var target = vm.Computers[0];

        vm.SetComputerEnabled(target, false);

        Assert.True(target.IsEnabled);
        Assert.Empty(storage.SavedSnapshots);
        Assert.Equal("Управление компьютерами", title);
        Assert.Contains("disk full", error);
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

    [Fact]
    public void ApplyConnectionsSourceChange_WhenSucceeded_ReplacesComputersAndStorage()
    {
        var initialStorage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "Local", Host = "10.0.0.1", ApiKey = "local-key", CertThumbprint = "LOCAL" }
            ]
        };
        var replacementStorage = new FakeComputerStorageService();
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(initialStorage, poller);

        InvokeApplyConnectionsSourceChange(
            vm,
            succeeded: true,
            replacementStorage,
            [
                new ComputerConfig { Name = "Shared", Host = "10.0.0.2", ApiKey = "shared-key", CertThumbprint = "SHARED" }
            ]);

        Assert.Single(vm.Computers);
        Assert.Equal("Shared", vm.Computers[0].Name);

        vm.SaveComputers();
        Assert.Single(replacementStorage.SavedSnapshots);
        Assert.Equal("Shared", replacementStorage.SavedSnapshots[0][0].Name);
        Assert.Empty(initialStorage.SavedSnapshots);
    }

    [Fact]
    public void ApplyConnectionsSourceChange_WhenPolling_RestartsPollerWithNewComputers()
    {
        var initialStorage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "Local", Host = "10.0.0.1", ApiKey = "local-key" }
            ]
        };
        var replacementStorage = new FakeComputerStorageService();
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(initialStorage, poller);

        InvokeApplyConnectionsSourceChange(
            vm,
            succeeded: true,
            replacementStorage,
            [
                new ComputerConfig { Name = "Shared A", Host = "10.0.0.2", ApiKey = "shared-a" },
                new ComputerConfig { Name = "Shared B", Host = "10.0.0.3", ApiKey = "shared-b" }
            ]);

        Assert.True(vm.IsPolling);
        Assert.Equal(2, poller.StartCalls.Count);
        Assert.Equal(1, poller.StopCalls);
        Assert.Equal(["Shared A", "Shared B"], poller.StartCalls[^1].ComputerNames);
        Assert.Equal(vm.RefreshInterval, poller.StartCalls[^1].IntervalSeconds);
    }

    [Fact]
    public void ApplyConnectionsSourceChange_WhenFailed_LeavesCurrentCollectionUnchanged()
    {
        var initialStorage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "Local", Host = "10.0.0.1", ApiKey = "local-key" }
            ]
        };
        var replacementStorage = new FakeComputerStorageService();
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(initialStorage, poller);

        InvokeApplyConnectionsSourceChange(
            vm,
            succeeded: false,
            replacementStorage,
            [
                new ComputerConfig { Name = "Shared", Host = "10.0.0.2", ApiKey = "shared-key" }
            ]);

        Assert.Single(vm.Computers);
        Assert.Equal("Local", vm.Computers[0].Name);

        vm.SaveComputers();
        Assert.Single(initialStorage.SavedSnapshots);
        Assert.Equal("Local", initialStorage.SavedSnapshots[0][0].Name);
        Assert.Empty(replacementStorage.SavedSnapshots);
        Assert.Single(poller.StartCalls);
        Assert.Equal(0, poller.StopCalls);
    }

    private MainViewModel CreateVm(IComputerStorageService storage, IScreenshotPollerService poller)
    {
        return new MainViewModel(
            storage,
            poller,
            new FakeViewerSettingsService(initialValue: false),
            new FakeAutostartService(initialValue: false));
    }

    private static void InvokeApplyConnectionsSourceChange(
        MainViewModel vm,
        bool succeeded,
        IComputerStorageService storage,
        IReadOnlyList<ComputerConfig> computers)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ApplyConnectionsSourceChange",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(vm, [succeeded, storage, computers]);
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

    private sealed class FakeComputerStorageService : IComputerStorageService
    {
        public List<ComputerConfig> LoadResult { get; set; } = [];
        public List<IReadOnlyList<ComputerConfig>> SavedSnapshots { get; } = [];
        public Exception? SaveException { get; set; }

        public List<ComputerConfig> Load()
        {
            return LoadResult.Select(config => new ComputerConfig
            {
                Id = config.Id,
                Name = config.Name,
                Host = config.Host,
                Port = config.Port,
                ApiKey = config.ApiKey,
                IsEnabled = config.IsEnabled,
                CertThumbprint = config.CertThumbprint
            }).ToList();
        }

        public void Save(IEnumerable<ComputerConfig> computers)
        {
            if (SaveException is not null)
                throw SaveException;

            SavedSnapshots.Add(computers.Select(config => new ComputerConfig
            {
                Id = config.Id,
                Name = config.Name,
                Host = config.Host,
                Port = config.Port,
                ApiKey = config.ApiKey,
                IsEnabled = config.IsEnabled,
                CertThumbprint = config.CertThumbprint
            }).ToList());
        }
    }

    private sealed class FakeScreenshotPollerService : IScreenshotPollerService
    {
        public List<(IReadOnlyList<string> ComputerNames, int IntervalSeconds)> StartCalls { get; } = [];
        public int StopCalls { get; private set; }

        public void Start(IEnumerable<ComputerViewModel> computers, int intervalSeconds)
        {
            StartCalls.Add((computers.Select(vm => vm.Name).ToList(), intervalSeconds));
        }

        public void Stop()
        {
            StopCalls++;
        }

        public void Dispose()
        {
        }
    }

    private class CountingSaveViewModel(ComputerStorageService s, ScreenshotPollerService p)
        : MainViewModel(s, p)
    {
        public int SaveCount { get; private set; }
        public override void SaveComputers() => SaveCount++;
    }
}
