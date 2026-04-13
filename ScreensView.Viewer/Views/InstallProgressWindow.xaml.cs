using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class InstallProgressWindow : Window
{
    public enum Mode { Install, Uninstall, UpdateAll, InstallDotNetRuntime }

    private readonly Mode _mode;
    private readonly List<ComputerConfig> _computers;
    private readonly string? _username;
    private readonly string? _password;
    private readonly ObservableCollection<LogEntry> _log = [];

    public InstallProgressWindow(Mode mode, List<ComputerConfig> computers, string? username = null, string? password = null)
    {
        InitializeComponent();
        _mode = mode;
        _computers = computers;
        _username = username;
        _password = password;
        LogList.ItemsSource = _log;
        Title = GetWindowTitle(mode);

        Loaded += async (_, _) => await RunAsync();
    }

    internal static string GetWindowTitle(Mode mode) => mode switch
    {
        Mode.Install => "Установка агента",
        Mode.Uninstall => "Удаление агента",
        Mode.UpdateAll => "Обновление агентов",
        Mode.InstallDotNetRuntime => "Установка .NET 8 Runtime",
        _ => "Операция"
    };

    internal static string BuildCompletionMessage(Mode mode, RuntimeInstallOutcome? runtimeOutcome = null) => mode switch
    {
        Mode.Install => "Агент установлен и запущен",
        Mode.Uninstall => "Агент удалён",
        Mode.UpdateAll => "Агент обновлён",
        Mode.InstallDotNetRuntime => runtimeOutcome?.Message
            ?? throw new ArgumentNullException(nameof(runtimeOutcome)),
        _ => string.Empty
    };

    private static string BuildCompletionStatus(Mode mode, RuntimeInstallOutcome? runtimeOutcome = null) => mode switch
    {
        Mode.Install => "Успешно",
        Mode.Uninstall => "Успешно",
        Mode.UpdateAll => "Успешно",
        Mode.InstallDotNetRuntime => runtimeOutcome?.Status switch
        {
            RuntimeInstallStatus.Installed => "Успешно",
            RuntimeInstallStatus.InstalledRebootRequired => "Предупреждение",
            RuntimeInstallStatus.SkippedNotRequired => "Пропуск",
            RuntimeInstallStatus.SkippedUnsupported => "Пропуск",
            _ => throw new ArgumentNullException(nameof(runtimeOutcome))
        },
        _ => string.Empty
    };

    private static AgentLogLevel BuildCompletionLevel(Mode mode, RuntimeInstallOutcome? runtimeOutcome = null) => mode switch
    {
        Mode.Install => AgentLogLevel.Success,
        Mode.Uninstall => AgentLogLevel.Success,
        Mode.UpdateAll => AgentLogLevel.Success,
        Mode.InstallDotNetRuntime => runtimeOutcome?.Level
            ?? throw new ArgumentNullException(nameof(runtimeOutcome)),
        _ => AgentLogLevel.Info
    };

    private async Task RunAsync()
    {
        var installer = new RemoteAgentInstaller(AddLog);

        await Parallel.ForEachAsync(_computers,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (computer, ct) =>
            {
                AddLog(computer.Name, "Выполняется...", string.Empty, AgentLogLevel.Info);
                try
                {
                    RuntimeInstallOutcome? runtimeOutcome = null;
                    switch (_mode)
                    {
                        case Mode.Install:
                            await installer.InstallAsync(computer, _username, _password);
                            break;
                        case Mode.Uninstall:
                            await installer.UninstallAsync(computer, _username, _password);
                            break;
                        case Mode.UpdateAll:
                            await installer.UpdateAsync(computer, _username, _password);
                            break;
                        case Mode.InstallDotNetRuntime:
                            runtimeOutcome = await installer.InstallDotNetRuntimeAsync(computer, _username, _password);
                            break;
                    }

                    AddLog(
                        computer.Name,
                        BuildCompletionStatus(_mode, runtimeOutcome),
                        BuildCompletionMessage(_mode, runtimeOutcome),
                        BuildCompletionLevel(_mode, runtimeOutcome));
                }
                catch (Exception ex)
                {
                    AddLog(computer.Name, "Ошибка", ex.Message, AgentLogLevel.Error);
                }
            });

        Dispatcher.Invoke(() => CloseButton.IsEnabled = true);
    }

    private void AddLog(string computer, string status, string message, AgentLogLevel level)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = _log.FirstOrDefault(e => e.Computer == computer);
            if (existing != null)
            {
                existing.Status  = status;
                existing.Message = message;
                existing.Level   = level;
            }
            else
            {
                _log.Add(new LogEntry { Computer = computer, Status = status, Message = message, Level = level });
            }
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public class LogEntry : INotifyPropertyChanged
    {
        private string _status  = string.Empty;
        private string _message = string.Empty;
        private AgentLogLevel _level = AgentLogLevel.Info;

        public string Computer { get; init; } = string.Empty;

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        public AgentLogLevel Level
        {
            get => _level;
            set { _level = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
