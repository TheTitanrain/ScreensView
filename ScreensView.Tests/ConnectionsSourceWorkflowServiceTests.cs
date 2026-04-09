using ScreensView.Shared.Models;
using ScreensView.Viewer.Helpers;
using ScreensView.Viewer.Services;
using ScreensView.Viewer.Views;

namespace ScreensView.Tests;

public sealed class ConnectionsSourceWorkflowServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ConnectionsSourceWorkflowServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void ResolveStartup_WithRememberedPassword_UsesExternalFileWithoutDialogs()
    {
        var externalPath = Path.Combine(_tempDirectory, "connections.svc");
        var expected = CreateComputers("Shared");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt("remembered")
        });
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (_, suppliedPassword) =>
            {
                Assert.Equal("remembered", suppliedPassword);
                return new FakeComputerStorageService { LoadResult = Clone(expected) };
            });
        var dialogs = new FakeConnectionsSourceDialogs();
        var workflow = new ConnectionsSourceWorkflowService(controller, settings, dialogs, _ => false);

        var result = workflow.ResolveStartup();

        Assert.NotNull(result);
        Assert.True(result!.UsesExternalFile);
        Assert.False(result.NeedsPassword);
        Assert.Equal(expected.Select(c => c.Name), result.Computers.Select(c => c.Name));
        Assert.Empty(dialogs.PasswordRequests);
        Assert.Equal(0, dialogs.StartupFallbackPrompts);
    }

    [Fact]
    public void ResolveStartup_WhenPasswordNeeded_RequestsPasswordAndLoadsExternalFile()
    {
        var externalPath = Path.Combine(_tempDirectory, "connections.svc");
        var expected = CreateComputers("Shared");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = string.Empty
        });
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (_, suppliedPassword) =>
            {
                Assert.Equal("manual-pass", suppliedPassword);
                return new FakeComputerStorageService { LoadResult = Clone(expected) };
            });
        var dialogs = new FakeConnectionsSourceDialogs();
        dialogs.PasswordResults.Enqueue(new ConnectionsFilePasswordDialogResult("manual-pass", RememberPassword: true));
        var workflow = new ConnectionsSourceWorkflowService(controller, settings, dialogs, _ => true);

        var result = workflow.ResolveStartup();

        Assert.NotNull(result);
        Assert.True(result!.UsesExternalFile);
        Assert.Equal(expected.Select(c => c.Name), result.Computers.Select(c => c.Name));
        Assert.Single(dialogs.PasswordRequests);
        Assert.Equal(externalPath, dialogs.PasswordRequests[0].FilePath);
        Assert.Equal(0, dialogs.StartupFallbackPrompts);
    }

    [Fact]
    public void ResolveStartup_WhenPasswordDialogCancelledAndUserChoosesLocal_FallsBackToLocal()
    {
        var externalPath = Path.Combine(_tempDirectory, "connections.svc");
        var expectedLocal = CreateComputers("Local");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = string.Empty
        });
        var localStorage = new FakeComputerStorageService { LoadResult = Clone(expectedLocal) };
        var controller = CreateController(
            settings,
            () => localStorage,
            (_, _) => new FakeComputerStorageService());
        var dialogs = new FakeConnectionsSourceDialogs();
        dialogs.PasswordResults.Enqueue(null);
        dialogs.StartupChoices.Enqueue(StartupExternalFileAction.SwitchToLocal);
        var workflow = new ConnectionsSourceWorkflowService(controller, settings, dialogs, _ => true);

        var result = workflow.ResolveStartup();

        Assert.NotNull(result);
        Assert.False(result!.UsesExternalFile);
        Assert.Equal(expectedLocal.Select(c => c.Name), result.Computers.Select(c => c.Name));
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
        Assert.Equal(1, dialogs.StartupFallbackPrompts);
    }

    [Fact]
    public void SelectConnectionsFile_WhenCreatingNewExternalFile_PersistsAndReturnsAppliedResult()
    {
        var targetPath = Path.Combine(_tempDirectory, "new-connections.svc");
        var settings = new FakeViewerSettingsService(new ViewerSettings());
        var localStorage = new FakeComputerStorageService { LoadResult = CreateComputers("Local") };
        var externalStorage = new FakeComputerStorageService();
        var controller = CreateController(settings, () => localStorage, (_, _) => externalStorage);
        controller.ResolveStartup();

        var dialogs = new FakeConnectionsSourceDialogs
        {
            PickConnectionsFileResult = targetPath,
            ConfirmExternalFileWarningResult = true
        };
        dialogs.PasswordResults.Enqueue(new ConnectionsFilePasswordDialogResult("new-pass", RememberPassword: false));
        var workflow = new ConnectionsSourceWorkflowService(controller, settings, dialogs, _ => false);
        var currentConnections = CreateComputers("A", "B");

        var result = workflow.SelectConnectionsFile(currentConnections);

        Assert.True(result.Applied);
        Assert.Same(externalStorage, result.Storage);
        Assert.Equal(currentConnections.Select(c => c.Name), result.Computers.Select(c => c.Name));
        Assert.Equal(targetPath, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
        Assert.Equal(1, dialogs.ExternalWarningPrompts);
        Assert.Equal(targetPath, dialogs.PickConnectionsFileResult);
    }

    [Fact]
    public void SwitchToLocalStorage_ClearsExternalFileAndReturnsAppliedResult()
    {
        var externalPath = Path.Combine(_tempDirectory, "connections.svc");
        var rememberedPassword = DpapiHelper.Encrypt("remembered");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = rememberedPassword
        });
        var currentConnections = CreateComputers("Shared");
        var localStorage = new FakeComputerStorageService();
        var externalStorage = new FakeComputerStorageService { LoadResult = Clone(currentConnections) };
        var controller = CreateController(settings, () => localStorage, (_, _) => externalStorage);
        controller.ResolveStartup();
        var workflow = new ConnectionsSourceWorkflowService(controller, settings, new FakeConnectionsSourceDialogs(), _ => true);

        var result = workflow.SwitchToLocalStorage(currentConnections);

        Assert.True(result.Applied);
        Assert.Same(localStorage, result.Storage);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
        Assert.Equal(currentConnections.Select(c => c.Name), localStorage.SavedSnapshots.Single().Select(c => c.Name));
    }

    [Fact]
    public void GetCurrentUiState_ReflectsCurrentSourceAndActionAvailability()
    {
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = Path.Combine(_tempDirectory, "connections.svc")
        });
        var workflow = new ConnectionsSourceWorkflowService(
            CreateController(settings, () => new FakeComputerStorageService(), (_, _) => new FakeComputerStorageService()),
            settings,
            new FakeConnectionsSourceDialogs(),
            _ => true);

        var externalState = workflow.GetCurrentUiState();

        Assert.True(externalState.UsesExternalFile);
        Assert.True(externalState.CanSwitchToLocal);
        Assert.Contains("connections.svc", externalState.DisplayText);

        settings.Current.ConnectionsFilePath = string.Empty;
        var localState = workflow.GetCurrentUiState();
        Assert.False(localState.UsesExternalFile);
        Assert.False(localState.CanSwitchToLocal);
        Assert.Contains(@"%AppData%\ScreensView\computers.json", localState.DisplayText);
    }

    private static ConnectionsStorageController CreateController(
        IViewerSettingsService settingsService,
        Func<IComputerStorageService> createLocalStorage,
        Func<string, string, IComputerStorageService> createEncryptedStorage)
    {
        return new ConnectionsStorageController(settingsService, createLocalStorage, createEncryptedStorage);
    }

    private static List<ComputerConfig> CreateComputers(params string[] names)
    {
        return names.Select((name, index) => new ComputerConfig
        {
            Name = name,
            Host = $"10.0.0.{index + 1}",
            Port = 5443 + index,
            ApiKey = $"key-{index + 1}",
            CertThumbprint = $"THUMB-{index + 1}"
        }).ToList();
    }

    private static List<ComputerConfig> Clone(IEnumerable<ComputerConfig> computers)
    {
        return computers.Select(computer => new ComputerConfig
        {
            Id = computer.Id,
            Name = computer.Name,
            Host = computer.Host,
            Port = computer.Port,
            ApiKey = computer.ApiKey,
            IsEnabled = computer.IsEnabled,
            CertThumbprint = computer.CertThumbprint
        }).ToList();
    }

    private sealed class FakeConnectionsSourceDialogs : IConnectionsSourceDialogs
    {
        public Queue<ConnectionsFilePasswordDialogResult?> PasswordResults { get; } = new();
        public Queue<StartupExternalFileAction> StartupChoices { get; } = new();
        public List<(ConnectionsFilePasswordMode Mode, string FilePath)> PasswordRequests { get; } = [];
        public int StartupFallbackPrompts { get; private set; }
        public int ExternalWarningPrompts { get; private set; }
        public bool ConfirmExternalFileWarningResult { get; set; } = true;
        public string? PickConnectionsFileResult { get; set; }
        public int OpenFailureMessages { get; private set; }
        public int CreateFailureMessages { get; private set; }
        public int SwitchToLocalFailureMessages { get; private set; }

        public bool ConfirmExternalFileWarning()
        {
            ExternalWarningPrompts++;
            return ConfirmExternalFileWarningResult;
        }

        public string? PickConnectionsFile(string fileName, string? initialDirectory)
            => PickConnectionsFileResult;

        public ConnectionsFilePasswordDialogResult? RequestPassword(ConnectionsFilePasswordMode mode, string filePath)
        {
            PasswordRequests.Add((mode, filePath));
            return PasswordResults.Count > 0 ? PasswordResults.Dequeue() : null;
        }

        public void ShowOpenExternalFileFailed(bool needsPassword)
        {
            OpenFailureMessages++;
        }

        public void ShowCreateExternalFileFailed()
        {
            CreateFailureMessages++;
        }

        public void ShowSwitchToLocalFailed()
        {
            SwitchToLocalFailureMessages++;
        }

        public StartupExternalFileAction AskStartupExternalFileFallback()
        {
            StartupFallbackPrompts++;
            return StartupChoices.Count > 0 ? StartupChoices.Dequeue() : StartupExternalFileAction.Cancel;
        }
    }

    private sealed class FakeViewerSettingsService(ViewerSettings initialSettings) : IViewerSettingsService
    {
        public ViewerSettings Current { get; set; } = CloneSettings(initialSettings);

        public ViewerSettings Load() => CloneSettings(Current);

        public void Save(ViewerSettings settings)
        {
            Current = CloneSettings(settings);
        }

        private static ViewerSettings CloneSettings(ViewerSettings settings)
        {
            return new ViewerSettings
            {
                LaunchAtStartup = settings.LaunchAtStartup,
                RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
                LlmCheckIntervalMinutes = settings.LlmCheckIntervalMinutes,
                LlmEnabled = settings.LlmEnabled,
                SelectedModelId = settings.SelectedModelId,
                LlamaServerBackend = settings.LlamaServerBackend,
                ConnectionsFilePath = settings.ConnectionsFilePath,
                ConnectionsFilePasswordEncrypted = settings.ConnectionsFilePasswordEncrypted
            };
        }
    }

    private sealed class FakeComputerStorageService : IComputerStorageService
    {
        public List<ComputerConfig> LoadResult { get; set; } = [];
        public Exception? LoadException { get; set; }
        public Exception? SaveException { get; set; }
        public List<IReadOnlyList<ComputerConfig>> SavedSnapshots { get; } = [];

        public List<ComputerConfig> Load()
        {
            if (LoadException is not null)
                throw LoadException;

            return Clone(LoadResult);
        }

        public void Save(IEnumerable<ComputerConfig> computers)
        {
            if (SaveException is not null)
                throw SaveException;

            SavedSnapshots.Add(Clone(computers));
        }
    }
}
