using System.IO;
using System.Windows;
using Microsoft.Win32;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Views;

namespace ScreensView.Viewer.Services;

internal interface IConnectionsSourceDialogs
{
    bool ConfirmExternalFileWarning();
    string? PickConnectionsFile(string fileName, string? initialDirectory);
    ConnectionsFilePasswordDialogResult? RequestPassword(ConnectionsFilePasswordMode mode, string filePath);
    void ShowOpenExternalFileFailed(bool needsPassword);
    void ShowCreateExternalFileFailed();
    void ShowSwitchToLocalFailed();
    StartupExternalFileAction AskStartupExternalFileFallback();
}

internal enum StartupExternalFileAction
{
    Retry,
    SwitchToLocal,
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
    Func<string, bool>? fileExists = null)
{
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    public ResolveConnectionsSourceResult? ResolveStartup()
    {
        var startup = controller.ResolveStartup();
        while (startup.NeedsPassword)
        {
            var settings = settingsService.Load();
            var passwordResult = dialogs.RequestPassword(
                ConnectionsFilePasswordMode.OpenExisting,
                settings.ConnectionsFilePath);

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

        var settings = settingsService.Load();
        var fileName = string.IsNullOrWhiteSpace(settings.ConnectionsFilePath)
            ? "connections.svc"
            : Path.GetFileName(settings.ConnectionsFilePath);
        var initialDirectory = GetInitialDirectory(settings.ConnectionsFilePath);
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
        var settings = settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ConnectionsFilePath))
        {
            return new ConnectionsSourceUiState(
                @"Локальный файл: %AppData%\ScreensView\computers.json",
                "Источник меняется в окне «Настройки».",
                UsesExternalFile: false,
                CanSwitchToLocal: false);
        }

        return new ConnectionsSourceUiState(
            settings.ConnectionsFilePath,
            "Источник меняется в окне «Настройки».",
            UsesExternalFile: true,
            CanSwitchToLocal: true);
    }

    private ConnectionsSourceChangeResult OpenExistingExternalFile(string filePath)
    {
        while (true)
        {
            var passwordResult = dialogs.RequestPassword(ConnectionsFilePasswordMode.OpenExisting, filePath);
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
        var passwordResult = dialogs.RequestPassword(ConnectionsFilePasswordMode.CreateNew, filePath);
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

    public ConnectionsFilePasswordDialogResult? RequestPassword(ConnectionsFilePasswordMode mode, string filePath)
    {
        var dialog = new ConnectionsFilePasswordWindow(mode, filePath);
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
