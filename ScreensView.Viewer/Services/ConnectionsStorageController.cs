using System.Security.Cryptography;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Helpers;

namespace ScreensView.Viewer.Services;

internal sealed class ConnectionsStorageController(
    IViewerSettingsService settingsService,
    Func<IComputerStorageService> createLocalStorage,
    Func<string, string, IComputerStorageService> createEncryptedStorage)
{
    public IComputerStorageService? ActiveStorage { get; private set; }
    public ConnectionsSourceState ActiveSourceState { get; private set; } = ConnectionsSourceState.Local;

    public ResolveConnectionsSourceResult ResolveStartup()
    {
        var settings = settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ConnectionsFilePath))
        {
            var localStorage = createLocalStorage();
            var computers = localStorage.Load();
            ActiveStorage = localStorage;
            ActiveSourceState = ConnectionsSourceState.Local;
            return new ResolveConnectionsSourceResult(localStorage, computers, usesExternalFile: false, needsPassword: false);
        }

        ActiveSourceState = ConnectionsSourceState.PersistentExternal(settings.ConnectionsFilePath);
        if (string.IsNullOrWhiteSpace(settings.ConnectionsFilePasswordEncrypted))
        {
            ActiveStorage = null;
            return new ResolveConnectionsSourceResult(storage: null, computers: [], usesExternalFile: true, needsPassword: true);
        }

        try
        {
            var password = DpapiHelper.Decrypt(settings.ConnectionsFilePasswordEncrypted);
            var externalStorage = createEncryptedStorage(settings.ConnectionsFilePath, password);
            var computers = externalStorage.Load();
            ActiveStorage = externalStorage;
            ActiveSourceState = ConnectionsSourceState.PersistentExternal(settings.ConnectionsFilePath);
            return new ResolveConnectionsSourceResult(externalStorage, computers, usesExternalFile: true, needsPassword: false);
        }
        catch (Exception ex) when (ex is EncryptedComputerStoragePasswordException or CryptographicException or FormatException)
        {
            settings.ConnectionsFilePasswordEncrypted = string.Empty;
            settingsService.Save(settings);
            ActiveStorage = null;
            return new ResolveConnectionsSourceResult(storage: null, computers: [], usesExternalFile: true, needsPassword: true);
        }
    }

    public SwitchConnectionsSourceResult OpenExternalFile(string filePath, string password, bool rememberPassword)
    {
        return OpenExternalFileCore(
            filePath,
            password,
            persistSettings: true,
            rememberPassword,
            ConnectionsSourceState.PersistentExternal(filePath));
    }

    public SwitchConnectionsSourceResult OpenExternalFileTemporarily(string filePath, string password)
    {
        return OpenExternalFileCore(
            filePath,
            password,
            persistSettings: false,
            rememberPassword: false,
            ConnectionsSourceState.TemporaryExternal(filePath));
    }

    public SwitchConnectionsSourceResult SwitchToExternalFile(
        string filePath,
        string password,
        bool rememberPassword,
        IReadOnlyList<ComputerConfig> currentConnections)
    {
        var previousStorage = ActiveStorage;
        var previousSourceState = ActiveSourceState;
        var externalStorage = createEncryptedStorage(filePath, password);

        try
        {
            externalStorage.Save(currentConnections);

            var settings = settingsService.Load();
            settings.ConnectionsFilePath = filePath;
            settings.ConnectionsFilePasswordEncrypted = rememberPassword ? DpapiHelper.Encrypt(password) : string.Empty;
            settingsService.Save(settings);

            ActiveStorage = externalStorage;
            ActiveSourceState = ConnectionsSourceState.PersistentExternal(filePath);
            return new SwitchConnectionsSourceResult(
                succeeded: true,
                storage: externalStorage,
                computers: Clone(currentConnections),
                usesExternalFile: true,
                needsPassword: false);
        }
        catch
        {
            ActiveStorage = previousStorage;
            ActiveSourceState = previousSourceState;
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: previousSourceState.UsesExternalFile,
                needsPassword: false);
        }
    }

    public SwitchConnectionsSourceResult SwitchToLocalStorage(IReadOnlyList<ComputerConfig> currentConnections)
    {
        var previousStorage = ActiveStorage;
        var previousSourceState = ActiveSourceState;
        var localStorage = createLocalStorage();

        try
        {
            localStorage.Save(currentConnections);

            var settings = settingsService.Load();
            settings.ConnectionsFilePath = string.Empty;
            settings.ConnectionsFilePasswordEncrypted = string.Empty;
            settingsService.Save(settings);

            ActiveStorage = localStorage;
            ActiveSourceState = ConnectionsSourceState.Local;
            return new SwitchConnectionsSourceResult(
                succeeded: true,
                storage: localStorage,
                computers: Clone(currentConnections),
                usesExternalFile: false,
                needsPassword: false);
        }
        catch
        {
            ActiveStorage = previousStorage;
            ActiveSourceState = previousSourceState;
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: previousSourceState.UsesExternalFile,
                needsPassword: false);
        }
    }

    private SwitchConnectionsSourceResult OpenExternalFileCore(
        string filePath,
        string password,
        bool persistSettings,
        bool rememberPassword,
        ConnectionsSourceState targetSourceState)
    {
        var previousStorage = ActiveStorage;
        var previousSourceState = ActiveSourceState;

        try
        {
            var externalStorage = createEncryptedStorage(filePath, password);
            var computers = externalStorage.Load();

            if (persistSettings)
            {
                var settings = settingsService.Load();
                settings.ConnectionsFilePath = filePath;
                settings.ConnectionsFilePasswordEncrypted = rememberPassword ? DpapiHelper.Encrypt(password) : string.Empty;
                settingsService.Save(settings);
            }

            ActiveStorage = externalStorage;
            ActiveSourceState = targetSourceState;
            return new SwitchConnectionsSourceResult(
                succeeded: true,
                storage: externalStorage,
                computers: computers,
                usesExternalFile: true,
                needsPassword: false);
        }
        catch (EncryptedComputerStoragePasswordException)
        {
            ActiveStorage = previousStorage;
            ActiveSourceState = previousSourceState;
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: true,
                needsPassword: true);
        }
        catch
        {
            ActiveStorage = previousStorage;
            ActiveSourceState = previousSourceState;
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: previousSourceState.UsesExternalFile,
                needsPassword: false);
        }
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
}

internal sealed record ConnectionsSourceState(
    bool UsesExternalFile,
    string? FilePath,
    bool IsTemporaryOverride)
{
    public static ConnectionsSourceState Local { get; } = new(false, null, false);

    public static ConnectionsSourceState PersistentExternal(string filePath) =>
        new(true, filePath, false);

    public static ConnectionsSourceState TemporaryExternal(string filePath) =>
        new(true, filePath, true);
}

internal sealed class ResolveConnectionsSourceResult(
    IComputerStorageService? storage,
    IReadOnlyList<ComputerConfig> computers,
    bool usesExternalFile,
    bool needsPassword)
{
    public IComputerStorageService? Storage { get; } = storage;
    public IReadOnlyList<ComputerConfig> Computers { get; } = computers;
    public bool UsesExternalFile { get; } = usesExternalFile;
    public bool NeedsPassword { get; } = needsPassword;
}

internal sealed class SwitchConnectionsSourceResult(
    bool succeeded,
    IComputerStorageService? storage,
    IReadOnlyList<ComputerConfig> computers,
    bool usesExternalFile,
    bool needsPassword)
{
    public bool Succeeded { get; } = succeeded;
    public IComputerStorageService? Storage { get; } = storage;
    public IReadOnlyList<ComputerConfig> Computers { get; } = computers;
    public bool UsesExternalFile { get; } = usesExternalFile;
    public bool NeedsPassword { get; } = needsPassword;
}
