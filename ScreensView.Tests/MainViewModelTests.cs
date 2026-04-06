using CommunityToolkit.Mvvm.Input;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Models;
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
    public async Task RefreshNowCommand_WhenPolling_RunsOneShotRefreshWithoutRestartingPoller()
    {
        var storage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", ApiKey = "key-1", IsEnabled = true },
                new ComputerConfig { Name = "PC-2", Host = "10.0.0.2", ApiKey = "key-2", IsEnabled = false }
            ]
        };
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(storage, poller);
        var command = GetRefreshNowCommand(vm);

        await command.ExecuteAsync(null);

        Assert.True(vm.IsPolling);
        Assert.Single(poller.StartCalls);
        Assert.Equal(0, poller.StopCalls);
        Assert.Single(poller.RefreshCalls);
        Assert.Equal(["PC-1", "PC-2"], poller.RefreshCalls[0]);
    }

    [Fact]
    public async Task RefreshNowCommand_WhenPollingStopped_KeepsPollingStopped()
    {
        var storage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "PC-1", Host = "10.0.0.1", ApiKey = "key-1", IsEnabled = true }
            ]
        };
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(storage, poller);
        vm.TogglePollingCommand.Execute(null);
        var command = GetRefreshNowCommand(vm);

        await command.ExecuteAsync(null);

        Assert.False(vm.IsPolling);
        Assert.Single(poller.StartCalls);
        Assert.Equal(1, poller.StopCalls);
        Assert.Single(poller.RefreshCalls);
        Assert.Equal(["PC-1"], poller.RefreshCalls[0]);
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
    public void UpdateComputer_UpdatesAllFieldsOnViewModel()
    {
        var vm = CreateVm();
        vm.AddComputer(new ComputerConfig { Name = "Old", Host = "10.0.0.1", Port = 5443, ApiKey = "old-key", IsEnabled = true, CertThumbprint = "OLDTHUMB" });
        var target = vm.Computers[0];
        var updated = new ComputerConfig { Name = "New", Host = "10.0.0.2", Port = 6443, ApiKey = "new-key", IsEnabled = false, CertThumbprint = "NEWTHUMB" };

        vm.UpdateComputer(target, updated);

        Assert.Equal("New", target.Name);
        Assert.Equal("10.0.0.2", target.Host);
        Assert.Equal(6443, target.Port);
        Assert.Equal("new-key", target.ApiKey);
        Assert.False(target.IsEnabled);
        Assert.Equal("NEWTHUMB", target.CertThumbprint);
    }

    [Fact]
    public void UpdateComputer_PersistsAfterReload()
    {
        var storage = new ComputerStorageService(_tempFile);
        using (var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient())))
        {
            vm.AddComputer(new ComputerConfig { Name = "Old", Host = "10.0.0.1", Port = 5443, ApiKey = "k1", CertThumbprint = "THUMB1" });
            vm.UpdateComputer(vm.Computers[0], new ComputerConfig { Name = "Updated", Host = "10.0.0.9", Port = 5443, ApiKey = "k1", CertThumbprint = "THUMB2" });
        }

        using var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        Assert.Single(vm2.Computers);
        Assert.Equal("Updated", vm2.Computers[0].Name);
        Assert.Equal("10.0.0.9", vm2.Computers[0].Host);
        Assert.Equal("THUMB2", vm2.Computers[0].CertThumbprint);
    }

    [Fact]
    public void RemoveComputer_RemovesFromCollection()
    {
        var vm = CreateVm();
        vm.AddComputers([
            new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
            new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
        ]);
        var toRemove = vm.Computers[0];

        vm.RemoveComputer(toRemove);

        Assert.Single(vm.Computers);
        Assert.Equal("B", vm.Computers[0].Name);
    }

    [Fact]
    public void RemoveComputer_PersistsAfterReload()
    {
        var storage = new ComputerStorageService(_tempFile);
        using (var vm = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient())))
        {
            vm.AddComputers([
                new ComputerConfig { Name = "A", Host = "10.0.0.1", Port = 5443, ApiKey = "k1" },
                new ComputerConfig { Name = "B", Host = "10.0.0.2", Port = 5443, ApiKey = "k2" },
            ]);
            vm.RemoveComputer(vm.Computers[0]);
        }

        using var vm2 = new MainViewModel(storage, new ScreenshotPollerService(new AgentHttpClient()));
        Assert.Single(vm2.Computers);
        Assert.Equal("B", vm2.Computers[0].Name);
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
    public void UpdateComputer_WhenDisablingComputer_SetsDisabledStatus()
    {
        var initialStorage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "Workstation", Host = "10.0.0.10", ApiKey = "key", IsEnabled = true }
            ]
        };
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(initialStorage, poller);
        var target = vm.Computers.Single();

        vm.UpdateComputer(target, new ComputerConfig
        {
            Id = target.Id,
            Name = target.Name,
            Host = target.Host,
            Port = target.Port,
            ApiKey = target.ApiKey,
            IsEnabled = false,
            CertThumbprint = target.CertThumbprint
        });

        Assert.Equal(ComputerStatus.Disabled, target.Status);
        Assert.Equal("Компьютер отключён в Управлении компьютерами.", target.StatusMessage);
    }

    [Fact]
    public void UpdateComputer_WhenReEnablingComputer_ResetsStatusToUnknown()
    {
        var initialStorage = new FakeComputerStorageService
        {
            LoadResult =
            [
                new ComputerConfig { Name = "Workstation", Host = "10.0.0.10", ApiKey = "key", IsEnabled = false }
            ]
        };
        var poller = new FakeScreenshotPollerService();

        using var vm = CreateVm(initialStorage, poller);
        var target = vm.Computers.Single();

        vm.UpdateComputer(target, new ComputerConfig
        {
            Id = target.Id,
            Name = target.Name,
            Host = target.Host,
            Port = target.Port,
            ApiKey = target.ApiKey,
            IsEnabled = true,
            CertThumbprint = target.CertThumbprint
        });

        Assert.Equal(ComputerStatus.Unknown, target.Status);
        Assert.Equal(string.Empty, target.StatusMessage);
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

        using var vm = new MainViewModel(
            initialStorage, poller,
            new FakeViewerSettingsService(initialValue: false, refreshIntervalSeconds: 7),
            new FakeAutostartService(initialValue: false));

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
        Assert.Equal(7, poller.StartCalls[^1].IntervalSeconds);
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
        Assert.Single(poller.StartCalls); // only the initial Start from the constructor
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

    private MainViewModel CreateVmWithLlm(
        IViewerSettingsService? settingsService = null,
        IAutostartService? autostartService = null,
        ILlmCheckService? llmCheckService = null,
        IModelDownloadService? downloadService = null,
        Action<string, string>? reportError = null,
        ILlmInferenceService? inferenceService = null)
    {
        var storage = new ComputerStorageService(_tempFile);
        var poller = new ScreenshotPollerService(new AgentHttpClient());
        return new MainViewModel(
            storage, poller,
            settingsService ?? new ViewerSettingsService(_settingsFile),
            autostartService ?? new FakeAutostartService(false),
            reportError,
            llmCheckService ?? new FakeLlmCheckService(),
            downloadService ?? new FakeModelDownloadService(),
            inferenceService ?? new FakeLlmInferenceService());
    }

    [Fact]
    public void Constructor_LoadsLlmCheckIntervalFromSettings()
    {
        var settings = new FakeViewerSettingsService(false, llmCheckIntervalMinutes: 12);
        using var vm = CreateVmWithLlm(settingsService: settings);
        Assert.Equal(12, vm.LlmCheckIntervalMinutes);
    }

    [Fact]
    public void Constructor_WhenModelReadyAndLlmEnabled_StartsLlmCheckService()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(settingsService: settings, llmCheckService: llm, downloadService: download);
        Assert.Single(llm.StartCalls);
    }

    [Fact]
    public void Constructor_WhenValidationFails_DoesNotStartLlmCheckService_AndReportsError()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        var inference = new FakeLlmInferenceService
        {
            ValidateModelResult = new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.ModelLoad,
                "Ошибка загрузки модели",
                "Failed to load model",
                @"C:\models\model.gguf",
                @"C:\models\mmproj.gguf")
        };
        string? title = null;
        string? message = null;

        using var vm = CreateVmWithLlm(
            settingsService: settings,
            llmCheckService: llm,
            downloadService: download,
            inferenceService: inference,
            reportError: (reportedTitle, reportedMessage) =>
            {
                title = reportedTitle;
                message = reportedMessage;
            });

        Assert.Empty(llm.StartCalls);
        Assert.Equal("Model load", title);
        Assert.Contains("Ошибка загрузки модели", message);
        Assert.Equal("Ошибка загрузки модели", vm.ModelStatusText);
    }

    [Fact]
    public async Task Constructor_WhenValidationIsSlow_DoesNotBlockStartup()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        var inference = new FakeLlmInferenceService
        {
            ValidateModelTask = new TaskCompletionSource<LlmRuntimeLoadException?>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };

        var vmTask = Task.Run(() => CreateVmWithLlm(
            settingsService: settings,
            llmCheckService: llm,
            downloadService: download,
            inferenceService: inference));

        await Task.Delay(150);

        Assert.True(vmTask.IsCompleted);
        Assert.Empty(llm.StartCalls);

        inference.ValidateModelTask.TrySetResult(null);
        using var vm = await vmTask;
    }

    [Fact]
    public void Constructor_WhenModelReadyButLlmDisabled_DoesNotStartLlmCheckService()
    {
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download); // llmEnabled defaults to false
        Assert.Empty(llm.StartCalls);
    }

    [Fact]
    public void Constructor_WhenModelNotReady_DoesNotStartLlmCheckService()
    {
        var download = new FakeModelDownloadService { IsModelReady = false };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);
        Assert.Empty(llm.StartCalls);
    }

    [Fact]
    public void ModelReady_Event_WhenLlmEnabled_StartsLlmCheckService()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = false };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(settingsService: settings, llmCheckService: llm, downloadService: download);

        download.IsModelReady = true;
        download.FireModelReady();

        Assert.Single(llm.StartCalls);
    }

    [Fact]
    public void ModelReady_Event_WhenValidationFails_ReportsErrorAndDoesNotStartLlmCheckService()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = false };
        var llm = new FakeLlmCheckService();
        var inference = new FakeLlmInferenceService
        {
            ValidateModelResult = new LlmRuntimeLoadException(
                LlmRuntimeLoadStage.ProjectorLoad,
                "Ошибка загрузки модели",
                "Failed to load projector",
                @"C:\models\model.gguf",
                @"C:\models\mmproj.gguf")
        };
        string? title = null;
        string? message = null;

        using var vm = CreateVmWithLlm(
            settingsService: settings,
            llmCheckService: llm,
            downloadService: download,
            inferenceService: inference,
            reportError: (reportedTitle, reportedMessage) =>
            {
                title = reportedTitle;
                message = reportedMessage;
            });

        download.IsModelReady = true;
        download.FireModelReady();

        Assert.Empty(llm.StartCalls);
        Assert.Equal("Model load", title);
        Assert.Contains("Ошибка загрузки модели", message);
        Assert.Equal("Ошибка загрузки модели", vm.ModelStatusText);
    }

    [Fact]
    public void ModelReady_Event_WhenLlmDisabled_DoesNotStartLlmCheckService()
    {
        var download = new FakeModelDownloadService { IsModelReady = false };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);
        download.FireModelReady();
        Assert.Empty(llm.StartCalls);
    }

    [Fact]
    public void IsLlmEnabled_WhenEnabledAndModelReady_StartsService()
    {
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);
        vm.IsLlmEnabled = true;
        Assert.NotEmpty(llm.StartCalls);
    }

    [Fact]
    public void IsLlmEnabled_WhenDisabled_StopsService()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(settingsService: settings, llmCheckService: llm, downloadService: download);
        vm.IsLlmEnabled = false;
        Assert.NotEmpty(llm.StopCalls);
    }

    [Fact]
    public void IsLlmEnabled_WhenChanged_SavesSettings()
    {
        var settings = new FakeViewerSettingsService(false);
        using var vm = CreateVmWithLlm(settingsService: settings);
        vm.IsLlmEnabled = true;
        Assert.True(settings.Current.LlmEnabled);
    }

    [Fact]
    public void SelectedModel_WhenChanged_CallsSelectModelOnDownloadService()
    {
        var download = new FakeModelDownloadService();
        using var vm = CreateVmWithLlm(downloadService: download);
        var newModel = new ModelDefinition("test", "Test", "test.gguf", "https://example.com/test.gguf", null, null);
        vm.SelectedModel = newModel;
        Assert.Contains(newModel, download.SelectModelCalls);
    }

    [Fact]
    public void SelectedModel_WhenChanged_CallsResetOnInferenceService()
    {
        var inference = new FakeLlmInferenceService();
        using var vm = CreateVmWithLlm(inferenceService: inference);
        var newModel = new ModelDefinition("test", "Test", "test.gguf", "https://example.com/test.gguf", null, null);
        vm.SelectedModel = newModel;
        Assert.Equal(1, inference.ResetCalls);
    }

    [Fact]
    public void SelectedModel_WhenChanged_SavesSettings()
    {
        var settings = new FakeViewerSettingsService(false);
        using var vm = CreateVmWithLlm(settingsService: settings);
        var newModel = new ModelDefinition("test", "Test", "test.gguf", "https://example.com/test.gguf", null, null);
        vm.SelectedModel = newModel;
        Assert.Equal("test", settings.Current.SelectedModelId);
    }

    [Fact]
    public void SelectedModel_WhenChanged_StopsLlmCheckService()
    {
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(llmCheckService: llm);
        var newModel = new ModelDefinition("test", "Test", "test.gguf", "https://example.com/test.gguf", null, null);
        vm.SelectedModel = newModel;
        Assert.NotEmpty(llm.StopCalls);
    }

    [Fact]
    public void SelectedModel_WhenChanged_AndModelReadyAndValidationSucceeds_StartsLlmCheckService()
    {
        var settings = new FakeViewerSettingsService(false, llmEnabled: true);
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();

        using var vm = CreateVmWithLlm(settingsService: settings, llmCheckService: llm, downloadService: download);
        var baselineStarts = llm.StartCalls.Count;

        vm.SelectedModel = new ModelDefinition("test", "Test", "test.gguf", "https://example.com/test.gguf", null, null);

        Assert.True(llm.StartCalls.Count > baselineStarts);
    }

    [Fact]
    public void LlmCheckIntervalMinutes_WhenChanged_CallsUpdateInterval()
    {
        var download = new FakeModelDownloadService { IsModelReady = true };
        var llm = new FakeLlmCheckService();
        using var vm = CreateVmWithLlm(llmCheckService: llm, downloadService: download);

        vm.LlmCheckIntervalMinutes = 20;

        Assert.Contains(20, llm.UpdateIntervalCalls);
    }

    [Fact]
    public void LlmCheckIntervalMinutes_WhenChanged_SavesSettings()
    {
        var settings = new FakeViewerSettingsService(false);
        var download = new FakeModelDownloadService { IsModelReady = false };
        using var vm = CreateVmWithLlm(settingsService: settings, downloadService: download);
        var priorSaves = settings.SaveCalls;

        vm.LlmCheckIntervalMinutes = 10;

        Assert.True(settings.SaveCalls > priorSaves);
        Assert.Equal(10, settings.Current.LlmCheckIntervalMinutes);
    }

    [Fact]
    public void Dispose_StopsLlmCheckService()
    {
        var llm = new FakeLlmCheckService();
        var vm = CreateVmWithLlm(llmCheckService: llm);
        vm.Dispose();
        Assert.Single(llm.StopCalls);
    }

    [Fact]
    public void Dispose_CallsResetOnInferenceService()
    {
        var inference = new FakeLlmInferenceService();
        var vm = CreateVmWithLlm(inferenceService: inference);
        vm.Dispose();
        Assert.True(inference.ResetCalls > 0);
    }

    [Fact]
    public void UpdateComputer_PropagatesDescription()
    {
        using var vm = CreateVmWithLlm();
        vm.AddComputer(new ComputerConfig { Name = "X", Host = "1.1.1.1", Port = 5443, ApiKey = "k" });
        var computerVm = vm.Computers[0];
        var updated = computerVm.ToConfig();
        updated.Description = "new desc";

        vm.UpdateComputer(computerVm, updated);

        Assert.Equal("new desc", computerVm.Description);
    }

    [Fact]
    public void UpdateComputer_ResetsLastLlmCheck()
    {
        using var vm = CreateVmWithLlm();
        vm.AddComputer(new ComputerConfig { Name = "X", Host = "1.1.1.1", Port = 5443, ApiKey = "k" });
        var computerVm = vm.Computers[0];
        computerVm.LastLlmCheck = new LlmCheckResult(true, "ok", false, DateTime.Now);

        vm.UpdateComputer(computerVm, computerVm.ToConfig());

        Assert.Null(computerVm.LastLlmCheck);
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

    private static IAsyncRelayCommand GetRefreshNowCommand(MainViewModel vm)
    {
        var property = typeof(MainViewModel).GetProperty("RefreshNowCommand");
        Assert.NotNull(property);
        var command = property!.GetValue(vm);
        return Assert.IsAssignableFrom<IAsyncRelayCommand>(command);
    }

    private class FakeViewerSettingsService(bool initialValue, int refreshIntervalSeconds = 5,
        int llmCheckIntervalMinutes = 5, bool llmEnabled = false) : IViewerSettingsService
    {
        public ViewerSettings Current { get; private set; } = new()
        {
            LaunchAtStartup = initialValue,
            RefreshIntervalSeconds = refreshIntervalSeconds,
            LlmCheckIntervalMinutes = llmCheckIntervalMinutes,
            LlmEnabled = llmEnabled
        };
        public int SaveCalls { get; private set; }

        public ViewerSettings Load() => new()
        {
            LaunchAtStartup = Current.LaunchAtStartup,
            RefreshIntervalSeconds = Current.RefreshIntervalSeconds,
            LlmCheckIntervalMinutes = Current.LlmCheckIntervalMinutes,
            LlmEnabled = Current.LlmEnabled,
            SelectedModelId = Current.SelectedModelId
        };

        public void Save(ViewerSettings settings)
        {
            SaveCalls++;
            Current = new ViewerSettings
            {
                LaunchAtStartup = settings.LaunchAtStartup,
                RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
                LlmCheckIntervalMinutes = settings.LlmCheckIntervalMinutes,
                LlmEnabled = settings.LlmEnabled,
                SelectedModelId = settings.SelectedModelId
            };
        }
    }

    private sealed class FakeLlmCheckService : ILlmCheckService
    {
        public List<(IReadOnlyList<string> Names, int IntervalMinutes)> StartCalls { get; } = [];
        public List<int> UpdateIntervalCalls { get; } = [];
        public List<object> StopCalls { get; } = [];

        public void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes)
            => StartCalls.Add((computers.Select(vm => vm.Name).ToList(), intervalMinutes));

        public void UpdateInterval(int intervalMinutes)
            => UpdateIntervalCalls.Add(intervalMinutes);

        public void Stop() => StopCalls.Add(new object());
    }

    private sealed class FakeModelDownloadService : IModelDownloadService
    {
        public bool IsModelReady { get; set; }
        public string ModelPath => @"C:\models\model.gguf";
        public string ProjectorPath => @"C:\models\mmproj.gguf";
        public ModelDefinition SelectedModel { get; private set; } = ModelDefinition.Default;
        public List<ModelDefinition> SelectModelCalls { get; } = [];
        public void SelectModel(ModelDefinition model)
        {
            SelectedModel = model;
            SelectModelCalls.Add(model);
        }
        public event EventHandler? ModelReady;
        public void FireModelReady() => ModelReady?.Invoke(this, EventArgs.Empty);
        public Task DownloadAsync(IProgress<double> progress, CancellationToken ct) => Task.CompletedTask;
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
        public List<IReadOnlyList<string>> RefreshCalls { get; } = [];
        public int StopCalls { get; private set; }

        public void Start(IEnumerable<ComputerViewModel> computers, int intervalSeconds)
        {
            StartCalls.Add((computers.Select(vm => vm.Name).ToList(), intervalSeconds));
        }

        public Task RefreshNowAsync(IEnumerable<ComputerViewModel> computers)
        {
            RefreshCalls.Add(computers.Select(vm => vm.Name).ToList());
            return Task.CompletedTask;
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

    private class FakeLlmInferenceService : ILlmInferenceService
    {
        public int ResetCalls { get; private set; }
        public LlmRuntimeLoadException? ValidateModelResult { get; set; }
        public TaskCompletionSource<LlmRuntimeLoadException?>? ValidateModelTask { get; set; }

        public void Reset() => ResetCalls++;

        public Task<LlmRuntimeLoadException?> ValidateModelAsync(CancellationToken ct)
            => ValidateModelTask?.Task ?? Task.FromResult(ValidateModelResult);

        public Task<LlmCheckResult> AnalyzeAsync(System.Windows.Media.Imaging.BitmapImage screenshot, string description, CancellationToken ct)
            => Task.FromResult(new LlmCheckResult(false, "fake", false, DateTime.UtcNow));
    }
}
