using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int DefaultRefreshIntervalSeconds = 5;
    private const int MinRefreshIntervalSeconds = 1;
    private const int MaxRefreshIntervalSeconds = 60;
    private const string ModelLoadErrorStatusText = "Ошибка загрузки модели";
    private const string ModelMissingMessage = "Модель для распознавания не скачана. Откройте настройки и нажмите \"Скачать\".";
    private const string LlmDisabledMessage = "Распознавание экрана выключено. Включите его в настройках.";
    private const string BackendErrorTitle = "Бэкенд распознавания";

    private IComputerStorageService _storage;
    private readonly IScreenshotPollerService _poller;
    private readonly IViewerSettingsService _viewerSettingsService;
    private readonly IAutostartService _autostartService;
    private readonly Action<string, string>? _reportError;
    private readonly IViewerLogService _log;
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher;

    private ViewerSettings _viewerSettings = new();
    private bool _isSynchronizingAutostart;
    private readonly ILlmCheckService _llmCheckService;
    private readonly IModelDownloadService _downloadService;
    private readonly ILlmInferenceService _inferenceService;
    private readonly ILlamaServerBinaryService _binaryService;
    private readonly CancellationTokenSource _appCts = new();
    private string? _modelLoadErrorText;
    private int _llmValidationVersion;

    public ObservableCollection<ComputerViewModel> Computers { get; } = [];

    [ObservableProperty] private int _refreshInterval = DefaultRefreshIntervalSeconds;
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private bool _isAutostartEnabled;
    [ObservableProperty] private int _llmCheckIntervalMinutes = 5;
    [ObservableProperty] private double _modelDownloadProgress = -1;
    [ObservableProperty] private double _binaryDownloadProgress = -1;
    [ObservableProperty] private bool _isLlmEnabled;
    [ObservableProperty] private ModelDefinition _selectedModel = ModelDefinition.Default;
    [ObservableProperty] private string _selectedBackend = "cpu";

    public CancellationToken AppToken => _appCts.Token;

    public MainViewModel(IComputerStorageService storage, IScreenshotPollerService poller)
        : this(storage, poller, new ViewerSettingsService(), new AutostartService())
    {
    }

    internal MainViewModel(
        IComputerStorageService storage,
        IScreenshotPollerService poller,
        IViewerSettingsService viewerSettingsService,
        IAutostartService autostartService,
        Action<string, string>? reportError = null,
        ILlmCheckService? llmCheckService = null,
        IModelDownloadService? downloadService = null,
        ILlmInferenceService? inferenceService = null,
        ILlamaServerBinaryService? binaryService = null,
        IViewerLogService? log = null)
    {
        _uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _storage = storage;
        _poller = poller;
        _viewerSettingsService = viewerSettingsService;
        _autostartService = autostartService;
        _reportError = reportError;
        _log = log ?? new NullViewerLogService();

        foreach (var config in _storage.Load())
            Computers.Add(new ComputerViewModel(config));

        _viewerSettings = _viewerSettingsService.Load();
        _refreshInterval = NormalizeRefreshInterval(_viewerSettings.RefreshIntervalSeconds);
        _viewerSettings.RefreshIntervalSeconds = _refreshInterval;
        InitializeAutostartState();

        _poller.Start(Computers, _refreshInterval);
        _isPolling = true;

        // Load persisted LLM settings before creating inference service
        _isLlmEnabled = _viewerSettings.LlmEnabled;
        _selectedModel = ModelDefinition.Available.FirstOrDefault(m => m.Id == _viewerSettings.SelectedModelId)
                         ?? ModelDefinition.Default;
        _selectedBackend = _viewerSettings.LlamaServerBackend;

        _downloadService = downloadService ?? new ModelDownloadService();
        _downloadService.SelectModel(_selectedModel);

        _binaryService = binaryService ?? new LlamaServerBinaryService();
        if (inferenceService is null)
        {
            var processService = new LlamaServerProcessService(_log);
            var factory = new LlamaServerVisionRuntimeFactory(
                _binaryService, processService, () => _selectedBackend);
            _inferenceService = new LlmInferenceService(_downloadService, factory, _log);
        }
        else
        {
            _inferenceService = inferenceService;
        }
        _llmCheckService = llmCheckService ?? new LlmCheckService(_inferenceService, _log);

        _llmCheckIntervalMinutes = NormalizeLlmCheckInterval(
            _viewerSettings.LlmCheckIntervalMinutes);
        _viewerSettings.LlmCheckIntervalMinutes = _llmCheckIntervalMinutes;

        _downloadService.ModelReady += (_, _) => HandleModelReady();

        if (_isLlmEnabled && _downloadService.IsModelReady)
            BeginTryStartLlmService("startup");
    }

    [RelayCommand]
    private void TogglePolling()
    {
        if (IsPolling)
        {
            _poller.Stop();
            IsPolling = false;
        }
        else
        {
            _poller.Start(Computers, RefreshInterval);
            IsPolling = true;
        }
    }

    [RelayCommand]
    private Task RefreshNowAsync()
    {
        return _poller.RefreshNowAsync(Computers);
    }

    [RelayCommand(CanExecute = nameof(CanRunLlmNow), AllowConcurrentExecutions = false)]
    private async Task RunLlmNowAsync(CancellationToken ct)
    {
        var runToken = GetRunToken(ct);
        if (!await EnsureManualLlmRunReadyAsync("manual run all", runToken))
            return;

        await _llmCheckService.RunNowAsync(Computers, runToken);
    }

    partial void OnRefreshIntervalChanged(int value)
    {
        var normalizedValue = NormalizeRefreshInterval(value);
        if (value != normalizedValue)
        {
            RefreshInterval = normalizedValue;
            return;
        }

        _viewerSettings.RefreshIntervalSeconds = value;
        _viewerSettingsService.Save(_viewerSettings);

        if (IsPolling)
        {
            _poller.Stop();
            _poller.Start(Computers, value);
        }
    }

    partial void OnLlmCheckIntervalMinutesChanged(int value)
    {
        var normalized = NormalizeLlmCheckInterval(value);
        if (value != normalized)
        {
            LlmCheckIntervalMinutes = normalized;
            return;
        }

        _viewerSettings.LlmCheckIntervalMinutes = value;
        _viewerSettingsService.Save(_viewerSettings);

        _llmCheckService.UpdateInterval(value);
    }

    private bool IsReadyToStart =>
        _downloadService.IsModelReady && GetBackendCheck(_selectedBackend).IsReady;

    public string ModelStatusText =>
        !string.IsNullOrEmpty(_modelLoadErrorText) ? _modelLoadErrorText :
        _downloadService.IsModelReady ? "Готово ✓" : "Не скачана";

    public string BinaryStatusText
    {
        get
        {
            return FormatBinaryStatusText(GetBackendCheck(_selectedBackend));
        }
    }

    public bool CanRunLlmNow => _isLlmEnabled && IsReadyToStart;

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task DownloadBinaryAsync(CancellationToken ct)
    {
        var backend = _selectedBackend;
        _log.LogInfo("MainViewModel.DownloadBinary.Start", $"Downloading llama-server binary for backend='{backend}'.");

        var progress = new Progress<double>(p => BinaryDownloadProgress = p);
        try
        {
            await _binaryService.DownloadAsync(backend, progress, ct);
            BinaryDownloadProgress = -1;
            OnPropertyChanged(nameof(BinaryStatusText));
            NotifyLlmManualRunAvailabilityChanged();
            if (_isLlmEnabled && _downloadService.IsModelReady)
                BeginTryStartLlmService("binary downloaded", showBackendError: true);
        }
        catch (OperationCanceledException)
        {
            BinaryDownloadProgress = -1;
            _log.LogWarning("MainViewModel.DownloadBinary.Cancelled", $"Binary download cancelled for '{backend}'.");
        }
        catch (Exception ex)
        {
            BinaryDownloadProgress = -1;
            var msg = ex.InnerException is not null
                ? $"{ex.Message}\n\n{ex.InnerException.Message}"
                : ex.Message;
            _log.LogError("MainViewModel.DownloadBinary.Failed", $"Binary download failed for '{backend}'.", ex);
            ReportError("Binary download", msg);
        }
    }

    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = false)]
    private async Task DownloadModelAsync(CancellationToken ct)
    {
        var model = _selectedModel; // snapshot to prevent race
        _log.LogInfo("MainViewModel.DownloadModel.Start", $"Starting model download for '{model.Id}'.");
        ClearModelLoadError();
        _downloadService.SelectModel(model);
        NotifyLlmManualRunAvailabilityChanged();

        var progress = new Progress<double>(p => ModelDownloadProgress = p);
        try
        {
            await _downloadService.DownloadAsync(progress, ct);
            ModelDownloadProgress = -1;
            OnPropertyChanged(nameof(ModelStatusText));
            if (_isLlmEnabled)
                BeginTryStartLlmService("download complete", showBackendError: true);
        }
        catch (OperationCanceledException)
        {
            ModelDownloadProgress = -1;
            _log.LogWarning("MainViewModel.DownloadModel.Cancelled", $"Download cancelled for '{model.Id}'.");
        }
        catch (Exception ex)
        {
            ModelDownloadProgress = -1;
            var msg = ex.InnerException is not null
                ? $"{ex.Message}\n\n{ex.InnerException.Message}"
                : ex.Message;
            _log.LogError("MainViewModel.DownloadModel.Failed", $"Download failed for '{model.Id}'.", ex);
            ReportError("Model download", msg);
        }
    }

    partial void OnIsLlmEnabledChanged(bool value)
    {
        _log.LogInfo("MainViewModel.LlmEnabledChanged", $"LlmEnabled={value}.");
        _viewerSettings.LlmEnabled = value;
        _viewerSettingsService.Save(_viewerSettings);
        NotifyLlmManualRunAvailabilityChanged();

        if (value)
            BeginTryStartLlmService("toggle enabled", showBackendError: true);
        else if (!value)
        {
            InvalidateLlmValidation();
            _llmCheckService.Stop();
        }
    }

    partial void OnSelectedBackendChanged(string value)
    {
        _log.LogInfo("MainViewModel.BackendChanged", $"Backend changed to '{value}'.");
        _viewerSettings.LlamaServerBackend = value;
        _viewerSettingsService.Save(_viewerSettings);

        InvalidateLlmValidation();
        _llmCheckService.Stop();
        _inferenceService.Reset();
        OnPropertyChanged(nameof(BinaryStatusText));
        NotifyLlmManualRunAvailabilityChanged();

        if (_isLlmEnabled && _downloadService.IsModelReady)
            BeginTryStartLlmService("backend changed", showBackendError: true);
    }

    partial void OnSelectedModelChanged(ModelDefinition value)
    {
        _log.LogInfo("MainViewModel.SelectedModelChanged", $"Selected model changed to '{value.Id}'.");
        _viewerSettings.SelectedModelId = value.Id;
        _viewerSettingsService.Save(_viewerSettings);

        // Stop → reset runtime → switch model path → restart if conditions met
        InvalidateLlmValidation();
        _llmCheckService.Stop();
        _inferenceService.Reset();
        _downloadService.SelectModel(value);
        ClearModelLoadError();
        OnPropertyChanged(nameof(ModelStatusText));
        NotifyLlmManualRunAvailabilityChanged();

        if (_isLlmEnabled && _downloadService.IsModelReady)
            BeginTryStartLlmService("model changed", showBackendError: true);
    }

    partial void OnIsAutostartEnabledChanged(bool value)
    {
        if (_isSynchronizingAutostart)
            return;

        try
        {
            _autostartService.SetEnabled(value);
            _viewerSettings.LaunchAtStartup = value;
            _viewerSettingsService.Save(_viewerSettings);
        }
        catch (Exception ex)
        {
            SetAutostartState(!value);
            ReportError("Автозапуск", $"Не удалось изменить автозапуск: {ex.Message}");
        }
    }

    public void AddComputer(ComputerConfig config)
    {
        Computers.Add(new ComputerViewModel(config));
        SaveComputers();
    }

    public void AddComputers(IEnumerable<ComputerConfig> configs)
    {
        foreach (var config in configs)
            Computers.Add(new ComputerViewModel(config));
        SaveComputers();
    }

    public void UpdateComputer(ComputerViewModel vm, ComputerConfig config)
    {
        vm.Name = config.Name;
        vm.Host = config.Host;
        vm.Port = config.Port;
        vm.ApiKey = config.ApiKey;
        vm.IsEnabled = config.IsEnabled;
        vm.CertThumbprint = config.CertThumbprint;
        vm.Description = config.Description;
        vm.LastLlmCheck = null;
        SaveComputers();
    }

    public void SetComputerEnabled(ComputerViewModel vm, bool isEnabled)
    {
        if (vm.IsEnabled == isEnabled)
            return;

        var previousValue = vm.IsEnabled;
        vm.IsEnabled = isEnabled;

        try
        {
            SaveComputers();
        }
        catch (Exception ex)
        {
            vm.IsEnabled = previousValue;
            ReportError("Управление компьютерами", $"Не удалось изменить состояние компьютера '{vm.Name}': {ex.Message}");
        }
    }

    public void RemoveComputer(ComputerViewModel vm)
    {
        Computers.Remove(vm);
        SaveComputers();
    }

    public void RemoveComputers(IEnumerable<ComputerViewModel> vms)
    {
        foreach (var vm in vms.ToList())
            Computers.Remove(vm);
        SaveComputers();
    }

    public virtual void SaveComputers()
    {
        _storage.Save(Computers.Select(c => c.ToConfig()));
    }

    internal void ApplyConnectionsSourceChange(
        bool succeeded,
        IComputerStorageService storage,
        IReadOnlyList<ComputerConfig> computers)
    {
        if (!succeeded)
            return;

        var restartPolling = IsPolling;
        var restartLlm = _isLlmEnabled && IsReadyToStart;

        if (restartLlm)
            _llmCheckService.Stop();

        if (restartPolling)
            _poller.Stop();

        _storage = storage;
        Computers.Clear();
        foreach (var computer in computers)
            Computers.Add(new ComputerViewModel(computer));

        if (restartPolling)
            _poller.Start(Computers, RefreshInterval);

        if (restartLlm)
            BeginTryStartLlmService("connections source changed");
    }

    public void Dispose()
    {
        InvalidateLlmValidation();
        _poller.Dispose();
        _llmCheckService.Stop();
        _inferenceService.Reset();
        _appCts.Cancel();
        _appCts.Dispose();
    }

    internal async Task RunLlmNowForComputerAsync(ComputerViewModel? computer, CancellationToken ct = default)
    {
        if (computer is null)
            return;

        var runToken = GetRunToken(ct);
        if (!await EnsureManualLlmRunReadyAsync($"manual run '{computer.Name}'", runToken))
            return;

        await _llmCheckService.RunNowAsync(computer, runToken);
    }

    private void InitializeAutostartState()
    {
        try
        {
            var actualState = _autostartService.IsEnabled();
            SetAutostartState(actualState);

            if (_viewerSettings.LaunchAtStartup != actualState)
            {
                _viewerSettings.LaunchAtStartup = actualState;
                _viewerSettingsService.Save(_viewerSettings);
            }
        }
        catch (Exception ex)
        {
            SetAutostartState(_viewerSettings.LaunchAtStartup);
            ReportError("Автозапуск", $"Не удалось прочитать автозапуск: {ex.Message}");
        }
    }

    private void ReportError(string title, string message)
    {
        _reportError?.Invoke(title, message);
    }

    private void HandleModelReady()
    {
        _log.LogInfo("MainViewModel.ModelReady", $"ModelReady received. LlmEnabled={_isLlmEnabled}.");
        ClearModelLoadError();
        OnPropertyChanged(nameof(ModelStatusText));
        NotifyLlmManualRunAvailabilityChanged();

        if (_isLlmEnabled && _downloadService.IsModelReady)
            BeginTryStartLlmService("model ready");
    }

    private void BeginTryStartLlmService(string reason, bool showBackendError = false)
    {
        _log.LogInfo(
            "MainViewModel.LlmStartAttempt",
            $"Attempting to start LLM service. Reason='{reason}', Enabled={_isLlmEnabled}, FilesReady={_downloadService.IsModelReady}.");

        OnPropertyChanged(nameof(BinaryStatusText));

        if (!_isLlmEnabled || !_downloadService.IsModelReady)
            return;

        var backendCheck = GetBackendCheck(_selectedBackend);
        if (!backendCheck.IsReady)
        {
            HandleBackendUnavailable(reason, backendCheck, showDialog: showBackendError);
            return;
        }

        var version = Interlocked.Increment(ref _llmValidationVersion);
        var validationTask = _inferenceService.ValidateModelAsync(_appCts.Token);
        if (validationTask.IsCompleted)
        {
            HandleCompletedValidation(reason, version, validationTask);
            return;
        }

        _ = FinishValidationAndStartAsync(validationTask, reason, version);
    }

    private async Task FinishValidationAndStartAsync(
        Task<LlmRuntimeLoadException?> validationTask,
        string reason,
        int version)
    {
        try
        {
            var validationError = await validationTask.ConfigureAwait(false);
            RunOnUiThread(() => ApplyValidationResult(reason, version, validationError));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HandleCompletedValidation(
        string reason,
        int version,
        Task<LlmRuntimeLoadException?> validationTask)
    {
        try
        {
            ApplyValidationResult(reason, version, validationTask.GetAwaiter().GetResult());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyValidationResult(
        string reason,
        int version,
        LlmRuntimeLoadException? validationError)
    {
        if (version != _llmValidationVersion || !_isLlmEnabled || !IsReadyToStart)
            return;

        if (validationError is null)
        {
            ClearModelLoadError();
            _llmCheckService.Start(Computers, _llmCheckIntervalMinutes);
            _log.LogInfo("MainViewModel.LlmStartSuccess", $"LLM service started after '{reason}'.");
            return;
        }

        _llmCheckService.Stop();
        if (validationError.Stage == LlmRuntimeLoadStage.Backend)
        {
            _log.LogError(
                "MainViewModel.LlmStartRejected",
                $"LLM backend validation failed during '{reason}'. {validationError.DiagnosticMessage}",
                validationError);
            ReportError(BackendErrorTitle, validationError.UserMessage);
            return;
        }

        SetModelLoadError(ModelLoadErrorStatusText);
        _log.LogError(
            "MainViewModel.LlmStartRejected",
            $"LLM validation failed during '{reason}'. {validationError.DiagnosticMessage}",
            validationError);
        ReportError("Model load", $"{validationError.UserMessage}\n\n{validationError.DiagnosticMessage}");
    }

    private void InvalidateLlmValidation()
    {
        Interlocked.Increment(ref _llmValidationVersion);
    }

    private BackendCheckResult GetBackendCheck(string backend) =>
        _binaryService.CheckInstallation(backend);

    private void HandleBackendUnavailable(string reason, BackendCheckResult backendCheck, bool showDialog)
    {
        _llmCheckService.Stop();
        ClearModelLoadError();
        NotifyLlmManualRunAvailabilityChanged();
        _log.LogWarning(
            "MainViewModel.BackendUnavailable",
            $"Backend '{backendCheck.Backend}' is {backendCheck.State} during '{reason}'. Missing: {string.Join(", ", backendCheck.MissingArtifacts)}");

        if (showDialog)
            ReportError(BackendErrorTitle, backendCheck.UserMessage);
    }

    private static string FormatBinaryStatusText(BackendCheckResult backendCheck) => backendCheck.State switch
    {
        BackendInstallState.Missing => "Не скачан",
        BackendInstallState.Incomplete => "Скачан не полностью",
        _ when !string.IsNullOrWhiteSpace(backendCheck.InstalledVersion) => $"Готово ✓ ({backendCheck.InstalledVersion})",
        _ => "Готово ✓"
    };

    private void RunOnUiThread(Action action)
    {
        if (_uiDispatcher.CheckAccess())
            action();
        else
            _uiDispatcher.Invoke(action);
    }

    private void SetModelLoadError(string value)
    {
        if (_modelLoadErrorText == value)
            return;

        _modelLoadErrorText = value;
        OnPropertyChanged(nameof(ModelStatusText));
    }

    private void ClearModelLoadError()
    {
        if (string.IsNullOrEmpty(_modelLoadErrorText))
            return;

        _modelLoadErrorText = null;
        OnPropertyChanged(nameof(ModelStatusText));
    }

    private void NotifyLlmManualRunAvailabilityChanged()
    {
        RunOnUiThread(() =>
        {
            OnPropertyChanged(nameof(CanRunLlmNow));
            RunLlmNowCommand.NotifyCanExecuteChanged();
        });
    }

    private CancellationToken GetRunToken(CancellationToken ct) =>
        ct == default ? _appCts.Token : ct;

    private async Task<bool> EnsureManualLlmRunReadyAsync(string reason, CancellationToken ct)
    {
        if (!_isLlmEnabled)
        {
            ReportError("LLM", LlmDisabledMessage);
            return false;
        }

        if (!_downloadService.IsModelReady)
        {
            SetModelLoadError(ModelLoadErrorStatusText);
            ReportError("Model load", ModelMissingMessage);
            NotifyLlmManualRunAvailabilityChanged();
            return false;
        }

        var backendCheck = GetBackendCheck(_selectedBackend);
        if (!backendCheck.IsReady)
        {
            HandleBackendUnavailable(reason, backendCheck, showDialog: true);
            return false;
        }

        var validationError = await _inferenceService.ValidateModelAsync(ct).ConfigureAwait(false);
        if (validationError is null)
        {
            ClearModelLoadError();
            NotifyLlmManualRunAvailabilityChanged();
            return true;
        }

        _llmCheckService.Stop();
        if (validationError.Stage == LlmRuntimeLoadStage.Backend)
        {
            _log.LogError(
                "MainViewModel.LlmRunNowRejected",
                $"LLM backend validation failed during '{reason}'. {validationError.DiagnosticMessage}",
                validationError);
            ReportError(BackendErrorTitle, validationError.UserMessage);
            return false;
        }

        SetModelLoadError(ModelLoadErrorStatusText);
        _log.LogError(
            "MainViewModel.LlmRunNowRejected",
            $"LLM validation failed during '{reason}'. {validationError.DiagnosticMessage}",
            validationError);
        ReportError("Model load", $"{validationError.UserMessage}\n\n{validationError.DiagnosticMessage}");
        NotifyLlmManualRunAvailabilityChanged();
        return false;
    }

    private void SetAutostartState(bool value)
    {
        _isSynchronizingAutostart = true;
        IsAutostartEnabled = value;
        _isSynchronizingAutostart = false;
    }

    private static int NormalizeRefreshInterval(int value)
    {
        return value is >= MinRefreshIntervalSeconds and <= MaxRefreshIntervalSeconds
            ? value
            : DefaultRefreshIntervalSeconds;
    }

    private const int MinLlmCheckIntervalMinutes = 1;
    private const int MaxLlmCheckIntervalMinutes = 60;
    private const int DefaultLlmCheckIntervalMinutes = 5;

    private static int NormalizeLlmCheckInterval(int value) =>
        value is >= MinLlmCheckIntervalMinutes and <= MaxLlmCheckIntervalMinutes
            ? value
            : DefaultLlmCheckIntervalMinutes;

    internal void ReportDownloadError(string message) =>
        ReportError("Model download", message);
}
