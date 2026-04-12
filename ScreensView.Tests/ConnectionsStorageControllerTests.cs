using System.Reflection;
using System.Runtime.ExceptionServices;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Helpers;
using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class ConnectionsStorageControllerTests
{
    [Fact]
    public void ResolveStartup_WhenConnectionsFilePathIsEmpty_UsesLocalStorage()
    {
        var expectedComputers = CreateComputers("Local workstation");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = string.Empty,
            ConnectionsFilePasswordEncrypted = string.Empty
        });
        var localStorage = new FakeComputerStorageService { LoadResult = Clone(expectedComputers) };
        var externalFactoryCalls = new List<(string Path, string Password)>();
        var controller = CreateController(
            settings,
            () => localStorage,
            (path, password) =>
            {
                externalFactoryCalls.Add((path, password));
                return new FakeComputerStorageService();
            });

        var result = InvokeResolveStartup(controller);

        Assert.Same(localStorage, GetStorage(result));
        AssertComputerListsEqual(expectedComputers, GetComputers(result));
        Assert.False(GetBoolean(result, "UsesExternalFile"));
        Assert.False(GetBoolean(result, "NeedsPassword"));
        Assert.Equal(1, localStorage.LoadCalls);
        Assert.Empty(externalFactoryCalls);
        Assert.Same(localStorage, GetActiveStorage(controller));
    }

    [Fact]
    public void ResolveStartup_WithRememberedPassword_OpensEncryptedExternalFileWithoutPromptingAgain()
    {
        const string externalPath = @"C:\Shared\connections.svc";
        const string rememberedPassword = "remembered-password";

        var expectedComputers = CreateComputers("Shared workstation");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt(rememberedPassword)
        });
        var externalStorage = new FakeComputerStorageService { LoadResult = Clone(expectedComputers) };
        var externalFactoryCalls = new List<(string Path, string Password)>();
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (path, password) =>
            {
                externalFactoryCalls.Add((path, password));
                return externalStorage;
            });

        var result = InvokeResolveStartup(controller);

        Assert.True(GetBoolean(result, "UsesExternalFile"));
        Assert.False(GetBoolean(result, "NeedsPassword"));
        Assert.Same(externalStorage, GetStorage(result));
        AssertComputerListsEqual(expectedComputers, GetComputers(result));
        Assert.Equal([(externalPath, rememberedPassword)], externalFactoryCalls);
        Assert.Equal(0, settings.SaveCalls);
        Assert.Same(externalStorage, GetActiveStorage(controller));
    }

    [Fact]
    public void ResolveStartup_WithBadRememberedPassword_ClearsItAndRequiresManualPassword()
    {
        const string externalPath = @"C:\Shared\connections.svc";
        const string badRememberedPassword = "bad-remembered-password";

        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt(badRememberedPassword)
        });
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (_, _) => new FakeComputerStorageService
            {
                LoadException = new EncryptedComputerStoragePasswordException("Password is invalid.")
            });

        var result = InvokeResolveStartup(controller);

        Assert.True(GetBoolean(result, "UsesExternalFile"));
        Assert.True(GetBoolean(result, "NeedsPassword"));
        Assert.Null(GetStorage(result));
        Assert.Empty(GetComputers(result));
        Assert.Equal(externalPath, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
        Assert.Equal(1, settings.SaveCalls);
        Assert.Null(GetActiveStorage(controller));
    }

    [Fact]
    public void SwitchToExternalFile_ExportsCurrentConnectionsBeforePersistingPath()
    {
        const string externalPath = @"C:\Shared\new-connections.svc";
        const string password = "new-password";

        var currentConnections = CreateComputers("PC-1", "PC-2");
        var settings = new FakeViewerSettingsService(new ViewerSettings());
        var localStorage = new FakeComputerStorageService();
        var events = new List<string>();
        var externalFactoryCalls = new List<(string Path, string Password)>();
        settings.OnSave = _ => events.Add("settings-save");

        var externalStorage = new FakeComputerStorageService();
        externalStorage.OnSave = computers =>
        {
            events.Add("external-save");
            Assert.Equal(string.Empty, settings.Current.ConnectionsFilePath);
            Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
            AssertComputerListsEqual(currentConnections, computers);
        };

        var controller = CreateController(
            settings,
            () => localStorage,
            (path, suppliedPassword) =>
            {
                externalFactoryCalls.Add((path, suppliedPassword));
                return externalStorage;
            });
        InvokeResolveStartup(controller);

        var result = InvokeSwitchToExternalFile(controller, externalPath, password, rememberPassword: true, currentConnections);

        Assert.True(GetBoolean(result, "Succeeded"));
        Assert.True(GetBoolean(result, "UsesExternalFile"));
        Assert.Same(externalStorage, GetStorage(result));
        Assert.Equal(["external-save", "settings-save"], events);
        Assert.Equal([(externalPath, password)], externalFactoryCalls);
        Assert.Equal(externalPath, settings.Current.ConnectionsFilePath);
        Assert.Equal(password, DpapiHelper.Decrypt(settings.Current.ConnectionsFilePasswordEncrypted));
        Assert.Same(externalStorage, GetActiveStorage(controller));
        Assert.Single(externalStorage.SavedSnapshots);
    }

    [Fact]
    public void SwitchToExternalFile_WhenRememberPasswordIsFalse_DoesNotPersistPassword()
    {
        const string externalPath = @"C:\Shared\new-connections.svc";
        const string password = "session-only-password";

        var currentConnections = CreateComputers("Session workstation");
        var settings = new FakeViewerSettingsService(new ViewerSettings());
        var externalFactoryCalls = new List<(string Path, string Password)>();
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (path, suppliedPassword) =>
            {
                externalFactoryCalls.Add((path, suppliedPassword));
                return new FakeComputerStorageService();
            });
        InvokeResolveStartup(controller);

        var result = InvokeSwitchToExternalFile(controller, externalPath, password, rememberPassword: false, currentConnections);

        Assert.True(GetBoolean(result, "Succeeded"));
        Assert.True(GetBoolean(result, "UsesExternalFile"));
        Assert.Equal([(externalPath, password)], externalFactoryCalls);
        Assert.Equal(externalPath, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
    }

    [Fact]
    public void OpenExternalFileTemporarily_WhenPasswordIsCorrect_UsesRuntimeExternalWithoutSavingSettings()
    {
        const string savedPath = @"C:\Shared\saved-connections.svc";
        const string temporaryPath = @"C:\Shared\temporary-connections.svc";
        const string password = "temporary-password";

        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = savedPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt("saved-password")
        });
        var externalStorage = new FakeComputerStorageService
        {
            LoadResult = CreateComputers("Temporary workstation")
        };
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (path, suppliedPassword) =>
            {
                return path switch
                {
                    savedPath => new FakeComputerStorageService { LoadResult = CreateComputers("Saved workstation") },
                    temporaryPath when suppliedPassword == password => externalStorage,
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected storage request: {path}")
                };
            });
        InvokeResolveStartup(controller);

        var result = InvokeOpenExternalFileTemporarily(controller, temporaryPath, password);

        Assert.True(GetBoolean(result, "Succeeded"));
        Assert.True(GetBoolean(result, "UsesExternalFile"));
        Assert.Same(externalStorage, GetStorage(result));
        Assert.Equal(savedPath, settings.Current.ConnectionsFilePath);
        Assert.Equal("saved-password", DpapiHelper.Decrypt(settings.Current.ConnectionsFilePasswordEncrypted));
        Assert.Equal(0, settings.SaveCalls);
        Assert.Same(externalStorage, GetActiveStorage(controller));

        var activeSourceState = GetActiveSourceState(controller);
        Assert.True(GetBoolean(activeSourceState, "UsesExternalFile"));
        Assert.True(GetBoolean(activeSourceState, "IsTemporaryOverride"));
        Assert.Equal(temporaryPath, GetString(activeSourceState, "FilePath"));
    }

    [Fact]
    public void OpenExternalFileWithoutPersistingSettings_WhenPasswordIsCorrect_UsesPersistentRuntimeExternalWithoutSavingSettings()
    {
        const string savedPath = @"C:\Shared\saved-connections.svc";
        const string password = "remembered-password";

        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = savedPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt(password)
        });
        var externalStorage = new FakeComputerStorageService
        {
            LoadResult = CreateComputers("Saved workstation")
        };
        var controller = CreateController(
            settings,
            () => new FakeComputerStorageService(),
            (path, suppliedPassword) =>
            {
                Assert.Equal(savedPath, path);
                Assert.Equal(password, suppliedPassword);
                return externalStorage;
            });

        var result = InvokeOpenExternalFileWithoutPersistingSettings(controller, savedPath, password);

        Assert.True(GetBoolean(result, "Succeeded"));
        Assert.True(GetBoolean(result, "UsesExternalFile"));
        Assert.Same(externalStorage, GetStorage(result));
        Assert.Equal(0, settings.SaveCalls);

        var activeSourceState = GetActiveSourceState(controller);
        Assert.True(GetBoolean(activeSourceState, "UsesExternalFile"));
        Assert.False(GetBoolean(activeSourceState, "IsTemporaryOverride"));
        Assert.Equal(savedPath, GetString(activeSourceState, "FilePath"));
    }

    [Fact]
    public void SwitchToLocalStorage_ClearsExternalPathAndRememberedPassword()
    {
        const string externalPath = @"C:\Shared\connections.svc";
        const string rememberedPassword = "remembered-password";

        var currentConnections = CreateComputers("Shared workstation");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt(rememberedPassword)
        });
        var localStorage = new FakeComputerStorageService();
        var externalStorage = new FakeComputerStorageService { LoadResult = Clone(currentConnections) };
        var controller = CreateController(settings, () => localStorage, (_, _) => externalStorage);
        InvokeResolveStartup(controller);

        var result = InvokeSwitchToLocalStorage(controller, currentConnections);

        Assert.True(GetBoolean(result, "Succeeded"));
        Assert.False(GetBoolean(result, "UsesExternalFile"));
        Assert.Same(localStorage, GetStorage(result));
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
        Assert.Single(localStorage.SavedSnapshots);
        AssertComputerListsEqual(currentConnections, localStorage.SavedSnapshots[0]);
        Assert.Same(localStorage, GetActiveStorage(controller));
    }

    [Fact]
    public void SwitchToExternalFile_WhenExportFails_LeavesSettingsAndActiveSourceUnchanged()
    {
        const string originalPath = "";
        const string targetPath = @"C:\Shared\broken-connections.svc";

        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = originalPath,
            ConnectionsFilePasswordEncrypted = string.Empty
        });
        var localStorage = new FakeComputerStorageService { LoadResult = CreateComputers("Local workstation") };
        var failingExternalStorage = new FakeComputerStorageService
        {
            SaveException = new InvalidOperationException("disk full")
        };
        var controller = CreateController(settings, () => localStorage, (_, _) => failingExternalStorage);
        var startup = InvokeResolveStartup(controller);

        var result = InvokeSwitchToExternalFile(
            controller,
            targetPath,
            "new-password",
            rememberPassword: true,
            CreateComputers("Export me"));

        Assert.False(GetBoolean(result, "Succeeded"));
        Assert.Same(localStorage, GetStorage(startup));
        Assert.Same(localStorage, GetActiveStorage(controller));
        Assert.Equal(originalPath, settings.Current.ConnectionsFilePath);
        Assert.Equal(string.Empty, settings.Current.ConnectionsFilePasswordEncrypted);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public void SwitchToLocalStorage_WhenSaveFails_LeavesSettingsAndActiveSourceUnchanged()
    {
        const string externalPath = @"C:\Shared\connections.svc";
        const string rememberedPassword = "remembered-password";

        var currentConnections = CreateComputers("Shared workstation");
        var settings = new FakeViewerSettingsService(new ViewerSettings
        {
            ConnectionsFilePath = externalPath,
            ConnectionsFilePasswordEncrypted = DpapiHelper.Encrypt(rememberedPassword)
        });
        var failingLocalStorage = new FakeComputerStorageService
        {
            SaveException = new InvalidOperationException("disk full")
        };
        var externalStorage = new FakeComputerStorageService { LoadResult = Clone(currentConnections) };
        var controller = CreateController(settings, () => failingLocalStorage, (_, _) => externalStorage);
        var startup = InvokeResolveStartup(controller);

        var result = InvokeSwitchToLocalStorage(controller, currentConnections);

        Assert.False(GetBoolean(result, "Succeeded"));
        Assert.Same(externalStorage, GetStorage(startup));
        Assert.Same(externalStorage, GetActiveStorage(controller));
        Assert.Equal(externalPath, settings.Current.ConnectionsFilePath);
        Assert.Equal(rememberedPassword, DpapiHelper.Decrypt(settings.Current.ConnectionsFilePasswordEncrypted));
        Assert.Equal(0, settings.SaveCalls);
    }

    private static object CreateController(
        IViewerSettingsService settingsService,
        Func<IComputerStorageService> createLocalStorage,
        Func<string, string, IComputerStorageService> createEncryptedStorage)
    {
        var type = Type.GetType("ScreensView.Viewer.Services.ConnectionsStorageController, ScreensView.Viewer", throwOnError: false);
        Assert.NotNull(type);

        return Activator.CreateInstance(
                   type!,
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   args: [settingsService, createLocalStorage, createEncryptedStorage],
                   culture: null)
               ?? throw new InvalidOperationException("Unable to create ConnectionsStorageController.");
    }

    private static object InvokeResolveStartup(object controller)
    {
        var method = GetRequiredMethod(controller.GetType(), "ResolveStartup", parameterCount: 0);
        return Invoke(controller, method, []);
    }

    private static object InvokeSwitchToExternalFile(
        object controller,
        string filePath,
        string password,
        bool rememberPassword,
        IReadOnlyList<ComputerConfig> currentConnections)
    {
        var method = GetRequiredMethod(controller.GetType(), "SwitchToExternalFile", parameterCount: 4);
        return Invoke(controller, method, [filePath, password, rememberPassword, currentConnections]);
    }

    private static object InvokeSwitchToLocalStorage(object controller, IReadOnlyList<ComputerConfig> currentConnections)
    {
        var method = GetRequiredMethod(controller.GetType(), "SwitchToLocalStorage", parameterCount: 1);
        return Invoke(controller, method, [currentConnections]);
    }

    private static object InvokeOpenExternalFileTemporarily(object controller, string filePath, string password)
    {
        var method = GetRequiredMethod(controller.GetType(), "OpenExternalFileTemporarily", parameterCount: 2);
        return Invoke(controller, method, [filePath, password]);
    }

    private static object InvokeOpenExternalFileWithoutPersistingSettings(object controller, string filePath, string password)
    {
        var method = GetRequiredMethod(controller.GetType(), "OpenExternalFileWithoutPersistingSettings", parameterCount: 2);
        return Invoke(controller, method, [filePath, password]);
    }

    private static object Invoke(object target, MethodInfo method, object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments)
                   ?? throw new InvalidOperationException($"{method.Name} returned null.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo GetRequiredMethod(Type type, string name, int parameterCount)
    {
        var method = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == parameterCount);

        Assert.NotNull(method);
        return method!;
    }

    private static IComputerStorageService? GetActiveStorage(object controller)
    {
        var property = controller.GetType().GetProperty("ActiveStorage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (IComputerStorageService?)property!.GetValue(controller);
    }

    private static object GetActiveSourceState(object controller)
    {
        var property = controller.GetType().GetProperty("ActiveSourceState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(controller)
               ?? throw new InvalidOperationException("ActiveSourceState returned null.");
    }

    private static IComputerStorageService? GetStorage(object result)
    {
        var property = result.GetType().GetProperty("Storage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (IComputerStorageService?)property!.GetValue(result);
    }

    private static IReadOnlyList<ComputerConfig> GetComputers(object result)
    {
        var property = result.GetType().GetProperty("Computers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(result);
        Assert.NotNull(value);
        var typed = Assert.IsAssignableFrom<IEnumerable<ComputerConfig>>(value);
        return typed.ToList();
    }

    private static bool GetBoolean(object result, string propertyName)
    {
        var property = result.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(result);
        Assert.IsType<bool>(value);
        return (bool)value!;
    }

    private static string? GetString(object result, string propertyName)
    {
        var property = result.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (string?)property!.GetValue(result);
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

    private static void AssertComputerListsEqual(
        IReadOnlyList<ComputerConfig> expected,
        IReadOnlyList<ComputerConfig> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Host, actual[index].Host);
            Assert.Equal(expected[index].Port, actual[index].Port);
            Assert.Equal(expected[index].ApiKey, actual[index].ApiKey);
            Assert.Equal(expected[index].IsEnabled, actual[index].IsEnabled);
            Assert.Equal(expected[index].CertThumbprint, actual[index].CertThumbprint);
        }
    }

    private sealed class FakeViewerSettingsService(ViewerSettings initialSettings) : IViewerSettingsService
    {
        public ViewerSettings Current { get; private set; } = CloneSettings(initialSettings);
        public int SaveCalls { get; private set; }
        public Action<ViewerSettings>? OnSave { get; set; }

        public ViewerSettings Load() => CloneSettings(Current);

        public void Save(ViewerSettings settings)
        {
            SaveCalls++;
            Current = CloneSettings(settings);
            OnSave?.Invoke(CloneSettings(Current));
        }

        private static ViewerSettings CloneSettings(ViewerSettings settings)
        {
            return new ViewerSettings
            {
                LaunchAtStartup = settings.LaunchAtStartup,
                RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
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
        public int LoadCalls { get; private set; }
        public List<IReadOnlyList<ComputerConfig>> SavedSnapshots { get; } = [];
        public Action<IReadOnlyList<ComputerConfig>>? OnSave { get; set; }

        public List<ComputerConfig> Load()
        {
            LoadCalls++;
            if (LoadException is not null)
                throw LoadException;

            return Clone(LoadResult);
        }

        public void Save(IEnumerable<ComputerConfig> computers)
        {
            var snapshot = Clone(computers);
            OnSave?.Invoke(snapshot);
            if (SaveException is not null)
                throw SaveException;

            SavedSnapshots.Add(snapshot);
        }
    }
}
