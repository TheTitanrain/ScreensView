using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Views;

namespace ScreensView.Viewer.Services;

internal interface IConnectionsSourceDialogs
{
    bool ConfirmExternalFileWarning();
    string? PickConnectionsFile(string fileName, string? initialDirectory);
    ConnectionsFilePasswordDialogResult? RequestPassword(ConnectionsFilePasswordMode mode, string filePath, bool allowRememberPassword);
    void ShowOpenExternalFileFailed(bool needsPassword);
    void ShowCreateExternalFileFailed();
    void ShowSwitchToLocalFailed();
    StartupExternalFileAction AskStartupExternalFileFallback();
    StartupConnectionsFileOverrideChoice AskStartupConnectionsFileOverrideChoice(string overridePath, string? savedPath);
    void ShowStartupOverrideError(string message);
}

internal enum StartupExternalFileAction
{
    Retry,
    SwitchToLocal,
    Cancel
}

internal enum StartupConnectionsFileOverrideChoice
{
    MakePersistent,
    UseTemporarily,
    Cancel
}

internal sealed record ConnectionsFilePasswordDialogResult(string Password, bool RememberPassword);

internal sealed record ConnectionsSourceUiState(
    string DisplayText,
    string HintText,
    bool UsesExternalFile,
    bool CanSwitchToLocal);

internal sealed record ConnectionsSourceChangeResult(
    bool Applied,
    IComputerStorageService? Storage,
    IReadOnlyList<ComputerConfig> Computers)
{
    public static ConnectionsSourceChangeResult NoChange { get; } = new(false, null, []);
}

internal sealed class ConnectionsSourceWorkflowService(
    ConnectionsStorageController controller,
    IViewerSettingsService settingsService,
    IConnectionsSourceDialogs dialogs,
    Func<string, bool>? fileExists = null,
    Func<string, bool>? directoryExists = null)
{
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;
    private readonly Func<string, bool> _directoryExists = directoryExists ?? Directory.Exists;

    public ResolveConnectionsSourceResult? ResolveStartup(ViewerStartupOptions startupOptions)
    {
        if (!startupOptions.IsValid)
        {
            dialogs.ShowStartupOverrideError(startupOptions.ErrorMessage ?? "Некорректные параметры запуска.");
            return null;
        }

        if (!startupOptions.HasConnectionsFileOverride)
            return ResolveStartup();

        return ResolveStartupOverride(startupOptions.ConnectionsFilePath!);
    }

    public ResolveConnectionsSourceResult? ResolveStartup()
    {
        var startup = controller.ResolveStartup();
        while (startup.NeedsPassword)
        {
            var settings = settingsService.Load();
            var passwordResult = dialogs.RequestPassword(
                ConnectionsFilePasswordMode.OpenExisting,
                settings.ConnectionsFilePath,
                allowRememberPassword: true);

            if (passwordResult is not null)
            {
                var openResult = controller.OpenExternalFile(
                    settings.ConnectionsFilePath,
                    passwordResult.Password,
                    passwordResult.RememberPassword);

                if (openResult.Succeeded && openResult.Storage is not null)
                {
                    return new ResolveConnectionsSourceResult(
                        openResult.Storage,
                        openResult.Computers,
                        usesExternalFile: true,
                        needsPassword: false);
                }

                dialogs.ShowOpenExternalFileFailed(openResult.NeedsPassword);
                if (!openResult.NeedsPassword)
                    return null;

                continue;
            }

            switch (dialogs.AskStartupExternalFileFallback())
            {
                case StartupExternalFileAction.SwitchToLocal:
                    var fallbackSettings = settingsService.Load();
                    fallbackSettings.ConnectionsFilePath = string.Empty;
                    fallbackSettings.ConnectionsFilePasswordEncrypted = string.Empty;
                    settingsService.Save(fallbackSettings);
                    return controller.ResolveStartup();

                case StartupExternalFileAction.Retry:
                    continue;

                default:
                    return null;
            }
        }

        return startup;
    }

    public ConnectionsSourceChangeResult SelectConnectionsFile(IReadOnlyList<ComputerConfig> currentConnections)
    {
        if (!dialogs.ConfirmExternalFileWarning())
            return ConnectionsSourceChangeResult.NoChange;

        var preferredPath = controller.ActiveSourceState.UsesExternalFile && !string.IsNullOrWhiteSpace(controller.ActiveSourceState.FilePath)
            ? controller.ActiveSourceState.FilePath
            : settingsService.Load().ConnectionsFilePath;
        var fileName = string.IsNullOrWhiteSpace(preferredPath)
            ? "connections.svc"
            : Path.GetFileName(preferredPath);
        var initialDirectory = GetInitialDirectory(preferredPath);
        var selectedFile = dialogs.PickConnectionsFile(fileName, initialDirectory);

        if (string.IsNullOrWhiteSpace(selectedFile))
            return ConnectionsSourceChangeResult.NoChange;

        return _fileExists(selectedFile)
            ? OpenExistingExternalFile(selectedFile)
            : CreateNewExternalFile(selectedFile, currentConnections);
    }

    public ConnectionsSourceChangeResult SwitchToLocalStorage(IReadOnlyList<ComputerConfig> currentConnections)
    {
        var result = controller.SwitchToLocalStorage(currentConnections);
        if (!result.Succeeded || result.Storage is null)
        {
            dialogs.ShowSwitchToLocalFailed();
            return ConnectionsSourceChangeResult.NoChange;
        }

        return new ConnectionsSourceChangeResult(true, result.Storage, result.Computers);
    }

    public ConnectionsSourceUiState GetCurrentUiState()
    {
        var sourceState = controller.ActiveSourceState;
        if (!sourceState.UsesExternalFile || string.IsNullOrWhiteSpace(sourceState.FilePath))
        {
            return new ConnectionsSourceUiState(
                @"Локальный файл: %AppData%\ScreensView\computers.json",
                "Источник меняется в окне «Настройки».",
                UsesExternalFile: false,
                CanSwitchToLocal: false);
        }

        return new ConnectionsSourceUiState(
            sourceState.FilePath,
            sourceState.IsTemporaryOverride
                ? "Источник используется только на этот запуск. После перезапуска Viewer вернётся к сохранённому источнику."
                : "Источник меняется в окне «Настройки».",
            UsesExternalFile: true,
            CanSwitchToLocal: true);
    }

    private ResolveConnectionsSourceResult? ResolveStartupOverride(string overridePath)
    {
        if (!_fileExists(overridePath) || _directoryExists(overridePath))
        {
            dialogs.ShowStartupOverrideError($"Файл подключений '{overridePath}' не найден.");
            return null;
        }

        var settings = settingsService.Load();
        var savedPath = string.IsNullOrWhiteSpace(settings.ConnectionsFilePath)
            ? null
            : settings.ConnectionsFilePath;
        if (string.Equals(savedPath, overridePath, StringComparison.OrdinalIgnoreCase))
            return ResolveOverrideForSavedPath(settings, overridePath);

        var choice = dialogs.AskStartupConnectionsFileOverrideChoice(overridePath, savedPath);
        return choice switch
        {
            StartupConnectionsFileOverrideChoice.MakePersistent => ResolveOverrideWithPasswordPrompt(overridePath, persistSelection: true),
            StartupConnectionsFileOverrideChoice.UseTemporarily => ResolveOverrideWithPasswordPrompt(overridePath, persistSelection: false),
            _ => null
        };
    }

    private ResolveConnectionsSourceResult? ResolveOverrideForSavedPath(ViewerSettings settings, string overridePath)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConnectionsFilePasswordEncrypted))
        {
            try
            {
                var password = Helpers.DpapiHelper.Decrypt(settings.ConnectionsFilePasswordEncrypted);
                var openResult = controller.OpenExternalFileWithoutPersistingSettings(overridePath, password);
                if (openResult.Succeeded && openResult.Storage is not null)
                    return ToResolveResult(openResult);

                if (openResult.NeedsPassword)
                {
                    TryPersistSavedPassword(filePath: overridePath, password: null, rememberPassword: false);
                }
                else
                {
                    dialogs.ShowOpenExternalFileFailed(needsPassword: false);
                    return null;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                TryPersistSavedPassword(filePath: overridePath, password: null, rememberPassword: false);
            }
        }

        return ResolveOverrideForSavedPathWithPasswordPrompt(overridePath);
    }

    private ResolveConnectionsSourceResult? ResolveOverrideForSavedPathWithPasswordPrompt(string filePath)
    {
        while (true)
        {
            var passwordResult = dialogs.RequestPassword(
                ConnectionsFilePasswordMode.OpenExisting,
                filePath,
                allowRememberPassword: true);
            if (passwordResult is null)
                return null;

            var openResult = controller.OpenExternalFileWithoutPersistingSettings(filePath, passwordResult.Password);
            if (openResult.Succeeded && openResult.Storage is not null)
            {
                TryPersistSavedPassword(filePath, passwordResult.Password, passwordResult.RememberPassword);
                return ToResolveResult(openResult);
            }

            dialogs.ShowOpenExternalFileFailed(openResult.NeedsPassword);
            if (!openResult.NeedsPassword)
                return null;
        }
    }

    private ResolveConnectionsSourceResult? ResolveOverrideWithPasswordPrompt(string filePath, bool persistSelection)
    {
        while (true)
        {
            var passwordResult = dialogs.RequestPassword(
                ConnectionsFilePasswordMode.OpenExisting,
                filePath,
                allowRememberPassword: persistSelection);
            if (passwordResult is null)
                return null;

            var openResult = persistSelection
                ? controller.OpenExternalFile(filePath, passwordResult.Password, passwordResult.RememberPassword)
                : controller.OpenExternalFileTemporarily(filePath, passwordResult.Password);
            if (openResult.Succeeded && openResult.Storage is not null)
                return ToResolveResult(openResult);

            dialogs.ShowOpenExternalFileFailed(openResult.NeedsPassword);
            if (!openResult.NeedsPassword)
                return null;
        }
    }

    private void TryPersistSavedPassword(string filePath, string? password, bool rememberPassword)
    {
        try
        {
            var settings = settingsService.Load();
            settings.ConnectionsFilePath = filePath;
            settings.ConnectionsFilePasswordEncrypted =
                rememberPassword && !string.IsNullOrEmpty(password)
                    ? Helpers.DpapiHelper.Encrypt(password)
                    : string.Empty;
            settingsService.Save(settings);
        }
        catch
        {
        }
    }

    private ConnectionsSourceChangeResult OpenExistingExternalFile(string filePath)
    {
        while (true)
        {
            var passwordResult = dialogs.RequestPassword(
                ConnectionsFilePasswordMode.OpenExisting,
                filePath,
                allowRememberPassword: true);
            if (passwordResult is null)
                return ConnectionsSourceChangeResult.NoChange;

            var openResult = controller.OpenExternalFile(filePath, passwordResult.Password, passwordResult.RememberPassword);
            if (openResult.Succeeded && openResult.Storage is not null)
                return new ConnectionsSourceChangeResult(true, openResult.Storage, openResult.Computers);

            dialogs.ShowOpenExternalFileFailed(openResult.NeedsPassword);
            if (!openResult.NeedsPassword)
                return ConnectionsSourceChangeResult.NoChange;
        }
    }

    private ConnectionsSourceChangeResult CreateNewExternalFile(
        string filePath,
        IReadOnlyList<ComputerConfig> currentConnections)
    {
        var passwordResult = dialogs.RequestPassword(
            ConnectionsFilePasswordMode.CreateNew,
            filePath,
            allowRememberPassword: true);
        if (passwordResult is null)
            return ConnectionsSourceChangeResult.NoChange;

        var createResult = controller.SwitchToExternalFile(
            filePath,
            passwordResult.Password,
            passwordResult.RememberPassword,
            currentConnections);

        if (!createResult.Succeeded || createResult.Storage is null)
        {
            dialogs.ShowCreateExternalFileFailed();
            return ConnectionsSourceChangeResult.NoChange;
        }

        return new ConnectionsSourceChangeResult(true, createResult.Storage, createResult.Computers);
    }

    private static ResolveConnectionsSourceResult ToResolveResult(SwitchConnectionsSourceResult openResult)
    {
        return new ResolveConnectionsSourceResult(
            openResult.Storage!,
            openResult.Computers,
            usesExternalFile: openResult.UsesExternalFile,
            needsPassword: false);
    }

    private static string? GetInitialDirectory(string savedPath)
    {
        if (string.IsNullOrWhiteSpace(savedPath))
            return null;

        var directory = Path.GetDirectoryName(savedPath);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : null;
    }
}

internal sealed class ConnectionsSourceDialogs(Func<Window?>? ownerProvider = null) : IConnectionsSourceDialogs
{
    public bool ConfirmExternalFileWarning()
    {
        return MessageBox.Show(
                   GetOwner(),
                   "Файл подключений будет доступен всем, у кого есть сам файл и пароль к нему. Храните его только в папке с ограниченным доступом.",
                   "Внешний файл подключений",
                   MessageBoxButton.OKCancel,
                   MessageBoxImage.Warning)
               == MessageBoxResult.OK;
    }

    public string? PickConnectionsFile(string fileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Выберите файл подключений",
            Filter = "Files (*.json;*.svc)|*.json;*.svc|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = fileName
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    public ConnectionsFilePasswordDialogResult? RequestPassword(ConnectionsFilePasswordMode mode, string filePath, bool allowRememberPassword)
    {
        var dialog = new ConnectionsFilePasswordWindow(mode, filePath, allowRememberPassword);
        var owner = GetOwner();
        if (owner is not null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        return dialog.ShowDialog() == true
            ? new ConnectionsFilePasswordDialogResult(dialog.Password, dialog.RememberPassword)
            : null;
    }

    public void ShowOpenExternalFileFailed(bool needsPassword)
    {
        MessageBox.Show(
            GetOwner(),
            needsPassword
                ? "Не удалось открыть файл подключений. Проверьте пароль и попробуйте снова."
                : "Не удалось открыть файл подключений.",
            "Файл подключений",
            MessageBoxButton.OK,
            needsPassword ? MessageBoxImage.Warning : MessageBoxImage.Error);
    }

    public void ShowCreateExternalFileFailed()
    {
        MessageBox.Show(
            GetOwner(),
            "Не удалось создать внешний файл подключений.",
            "Файл подключений",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void ShowSwitchToLocalFailed()
    {
        MessageBox.Show(
            GetOwner(),
            "Не удалось переключиться на локальный файл подключений.",
            "Файл подключений",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public StartupExternalFileAction AskStartupExternalFileFallback()
    {
        var result = MessageBox.Show(
            GetOwner(),
            "Внешний файл подключений не открыт. Переключиться на локальный файл подключений?",
            "Файл подключений",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => StartupExternalFileAction.SwitchToLocal,
            MessageBoxResult.No => StartupExternalFileAction.Retry,
            _ => StartupExternalFileAction.Cancel
        };
    }

    public StartupConnectionsFileOverrideChoice AskStartupConnectionsFileOverrideChoice(string overridePath, string? savedPath)
    {
        var prompt = string.IsNullOrWhiteSpace(savedPath)
            ? $"Открыт Viewer с файлом подключений:\n{overridePath}\n\nСделать этот файл основным источником подключений?"
            : $"В настройках уже сохранён другой файл подключений:\n{savedPath}\n\nИспользовать вместо него файл:\n{overridePath}\n\nСделать новый файл основным источником подключений?";

        var result = MessageBox.Show(
            GetOwner(),
            $"{prompt}\n\nДа — сделать основным.\nНет — использовать только на этот запуск.\nОтмена — закрыть Viewer.",
            "Файл подключений",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => StartupConnectionsFileOverrideChoice.MakePersistent,
            MessageBoxResult.No => StartupConnectionsFileOverrideChoice.UseTemporarily,
            _ => StartupConnectionsFileOverrideChoice.Cancel
        };
    }

    public void ShowStartupOverrideError(string message)
    {
        MessageBox.Show(
            GetOwner(),
            message,
            "Файл подключений",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private Window? GetOwner()
    {
        var fromProvider = ownerProvider?.Invoke();
        if (fromProvider is not null)
            return fromProvider;

        var app = Application.Current;
        return app?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
               ?? app?.MainWindow;
    }
}
