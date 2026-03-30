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

    public ResolveConnectionsSourceResult ResolveStartup()
    {
        var settings = settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ConnectionsFilePath))
        {
            var localStorage = createLocalStorage();
            var computers = localStorage.Load();
            ActiveStorage = localStorage;
            return new ResolveConnectionsSourceResult(localStorage, computers, usesExternalFile: false, needsPassword: false);
        }

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
        var previousStorage = ActiveStorage;

        try
        {
            var externalStorage = createEncryptedStorage(filePath, password);
            var computers = externalStorage.Load();

            var settings = settingsService.Load();
            settings.ConnectionsFilePath = filePath;
            settings.ConnectionsFilePasswordEncrypted = rememberPassword ? DpapiHelper.Encrypt(password) : string.Empty;
            settingsService.Save(settings);

            ActiveStorage = externalStorage;
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
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: previousStorage is EncryptedComputerStorageService,
                needsPassword: false);
        }
    }

    public SwitchConnectionsSourceResult SwitchToExternalFile(
        string filePath,
        string password,
        bool rememberPassword,
        IReadOnlyList<ComputerConfig> currentConnections)
    {
        var previousStorage = ActiveStorage;
        var externalStorage = createEncryptedStorage(filePath, password);

        try
        {
            externalStorage.Save(currentConnections);

            var settings = settingsService.Load();
            settings.ConnectionsFilePath = filePath;
            settings.ConnectionsFilePasswordEncrypted = rememberPassword ? DpapiHelper.Encrypt(password) : string.Empty;
            settingsService.Save(settings);

            ActiveStorage = externalStorage;
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
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: previousStorage is EncryptedComputerStorageService,
                needsPassword: false);
        }
    }

    public SwitchConnectionsSourceResult SwitchToLocalStorage(IReadOnlyList<ComputerConfig> currentConnections)
    {
        var previousStorage = ActiveStorage;
        var localStorage = createLocalStorage();

        try
        {
            localStorage.Save(currentConnections);

            var settings = settingsService.Load();
            settings.ConnectionsFilePath = string.Empty;
            settings.ConnectionsFilePasswordEncrypted = string.Empty;
            settingsService.Save(settings);

            ActiveStorage = localStorage;
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
            return new SwitchConnectionsSourceResult(
                succeeded: false,
                storage: previousStorage,
                computers: [],
                usesExternalFile: previousStorage is EncryptedComputerStorageService,
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
