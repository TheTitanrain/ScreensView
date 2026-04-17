using System.Diagnostics;
using ScreensView.Viewer.Models;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Services;

public interface ILlmCheckService
{
    void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes);
    void UpdateInterval(int intervalMinutes);
    void Stop();
    Task RunNowAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct = default);
    Task RunNowAsync(ComputerViewModel computer, CancellationToken ct = default);
}

public class LlmCheckService : ILlmCheckService
{
    private const int MaxCachedChecksPerComputer = 16;
    private const int DefaultPerComputerTimeoutSeconds = 120;
    private static readonly TimeSpan DefaultPerComputerTimeout =
        TimeSpan.FromSeconds(DefaultPerComputerTimeoutSeconds);
    private const string TimeoutMessageTemplate =
        "Распознавание превысило лимит {0} секунд. Повторим в следующем цикле.";

    private readonly ILlmInferenceService _inference;
    private readonly IViewerLogService _log;
    private readonly TimeSpan _perComputerTimeout;
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private readonly Dictionary<Guid, List<CachedLlmCheckEntry>> _recentChecks = [];
    private volatile int _intervalMinutes = 5;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private IReadOnlyList<ComputerViewModel>? _computers;

    public LlmCheckService(ILlmInferenceService inference)
        : this(inference, null)
    {
    }

    internal LlmCheckService(
        ILlmInferenceService inference,
        IViewerLogService? log,
        TimeSpan? perComputerTimeout = null)
    {
        _inference = inference;
        _log = log ?? new NullViewerLogService();
        _perComputerTimeout = perComputerTimeout ?? DefaultPerComputerTimeout;
    }

    public void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes)
    {
        if (_cts is not null)
            return; // idempotent — already running

        ClearCache();
        _log.LogInfo("LlmCheckService.Start", $"Starting service for {computers.Count} computers, interval={intervalMinutes} min.");
        _computers = computers;
        SetServiceActive(computers, isActive: true);
        _intervalMinutes = intervalMinutes;
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public void UpdateInterval(int intervalMinutes)
    {
        _intervalMinutes = intervalMinutes; // volatile write — read by loop at next Task.Delay
    }

    public void Stop()
    {
        _log.LogInfo("LlmCheckService.Stop", "Stopping service.");
        if (_computers is not null)
            SetServiceActive(_computers, isActive: false);

        ClearCache();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public Task RunNowAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct = default)
    {
        _log.LogInfo("LlmCheckService.RunNowAll", $"Starting one-shot LLM check for {computers.Count} computers.");
        return RunCycleExclusiveAsync(computers, ct);
    }

    public Task RunNowAsync(ComputerViewModel computer, CancellationToken ct = default)
    {
        _log.LogInfo("LlmCheckService.RunNowSingle", $"Starting one-shot LLM check for '{computer.Name}'.");
        return RunCycleExclusiveAsync([computer], ct);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _log.LogInfo("LlmCheckService.RunCycle", "Starting LLM check cycle.");
            await RunCycleExclusiveAsync(_computers ?? [], ct);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCycleExclusiveAsync(IReadOnlyList<ComputerViewModel> computers, CancellationToken ct)
    {
        await _cycleGate.WaitAsync(ct);
        try
        {
            await RunCycleAsync(computers, ct);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    // Internal for testing — allows direct cycle execution without the delay
    internal async Task RunCycleAsync(IReadOnlyList<ComputerViewModel> computers,
        CancellationToken ct = default)
    {
        var snapshot = System.Windows.Application.Current is not null
            ? System.Windows.Application.Current.Dispatcher.Invoke(
                () => computers
                    .Where(vm => vm.IsEnabled)
                    .ToList())
            : computers
                .Where(vm => vm.IsEnabled)
                .ToList(); // test path: no Application.Current

        foreach (var vm in snapshot)
        {
            var screenshotCopy = vm.Screenshot;
            if (screenshotCopy is null)
            {
                _log.LogInfo("LlmCheckService.SkipNoScreenshot", $"Skipping '{vm.Name}' because screenshot is missing.");
                continue;
            }

            var descriptionAtStart = vm.Description;
            if (string.IsNullOrEmpty(descriptionAtStart))
            {
                _log.LogInfo("LlmCheckService.SkipNoDescription", $"Skipping '{vm.Name}' because description is missing.");
                continue;
            }

            if (vm.Status != ComputerStatus.Online || vm.LastUpdated is null)
            {
                SetOnDispatcher(() => vm.LastLlmCheck = null);
                _log.LogInfo(
                    "LlmCheckService.SkipStaleScreenshot",
                    $"Skipping '{vm.Name}' because screenshot is stale. Status={vm.Status}, LastUpdated={vm.LastUpdated?.ToString("O") ?? "null"}.");
                continue;
            }

            var screenshotTimestampAtStart = vm.LastUpdated.Value;
            var prepared = await InferenceImagePreprocessor.PrepareAsync(screenshotCopy).ConfigureAwait(false);
            if (TryGetCachedResult(vm.Id, descriptionAtStart, prepared.Hash64, out var cachedResult))
            {
                SetOnDispatcher(() => vm.LastLlmCheck = cachedResult);
                _log.LogInfo(
                    "LlmCheckService.CacheHit",
                    $"Reused cached result for '{vm.Name}'. description='{descriptionAtStart}', hash={prepared.Hash64}.");
                continue;
            }

            _log.LogInfo(
                "LlmCheckService.CacheMiss",
                $"No cached result for '{vm.Name}'. description='{descriptionAtStart}', hash={prepared.Hash64}.");
            SetOnDispatcher(() => vm.IsLlmChecking = true);

            using var timeoutCts = new CancellationTokenSource(_perComputerTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await _inference.AnalyzeAsync(screenshotCopy, descriptionAtStart, linked.Token);
                stopwatch.Stop();
                var isDescriptionUnchanged = false;

                SetOnDispatcher(() =>
                {
                    if (vm.Description == descriptionAtStart
                        && vm.Status == ComputerStatus.Online
                        && vm.LastUpdated == screenshotTimestampAtStart)
                    {
                        isDescriptionUnchanged = true;
                        vm.LastLlmCheck = result;
                    }
                });

                if (isDescriptionUnchanged && !result.IsError)
                    StoreCachedResult(vm.Id, descriptionAtStart, prepared.Hash64, screenshotTimestampAtStart, result);

                var outcome = result.IsError
                    ? "error"
                    : result.IsMatch ? "match" : "mismatch";
                _log.LogInfo(
                    "LlmCheckService.Result",
                    $"Computer='{vm.Name}', elapsedMs={stopwatch.ElapsedMilliseconds}, outcome={outcome}, stored={isDescriptionUnchanged}, description='{descriptionAtStart}', explanation='{result.Explanation}'.");

                if (result.IsError)
                {
                    _log.LogWarning(
                        "LlmCheckService.ResultError",
                        $"AnalyzeAsync returned error result for '{vm.Name}': {result.Explanation}");
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                stopwatch.Stop();
                var timeoutMessage = BuildTimeoutMessage();
                SetOnDispatcher(() =>
                {
                    if (vm.Description == descriptionAtStart)
                        vm.LastLlmCheck = new LlmCheckResult(false, timeoutMessage, IsError: true, DateTime.Now);
                });
                _log.LogWarning(
                    "LlmCheckService.Timeout",
                    $"AnalyzeAsync timed out for '{vm.Name}'. elapsedMs={stopwatch.ElapsedMilliseconds}, description='{descriptionAtStart}'.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                stopwatch.Stop();
                _log.LogInfo(
                    "LlmCheckService.Cancelled",
                    $"Cancelling LLM cycle for '{vm.Name}'. elapsedMs={stopwatch.ElapsedMilliseconds}, description='{descriptionAtStart}'.");
                break;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                SetOnDispatcher(() =>
                {
                    if (vm.Description == descriptionAtStart)
                        vm.LastLlmCheck = new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
                });
                _log.LogError(
                    "LlmCheckService.ResultError",
                    $"AnalyzeAsync failed for '{vm.Name}'. elapsedMs={stopwatch.ElapsedMilliseconds}, description='{descriptionAtStart}'.",
                    ex);
            }
            finally
            {
                SetOnDispatcher(() => vm.IsLlmChecking = false);
            }
        }
    }

    private static void SetOnDispatcher(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app is not null)
            app.Dispatcher.Invoke(action);
        else
            action(); // test path: no WPF dispatcher
    }

    private static void SetServiceActive(IEnumerable<ComputerViewModel> computers, bool isActive)
    {
        foreach (var vm in computers)
            SetOnDispatcher(() => vm.IsLlmServiceActive = isActive);
    }

    private bool TryGetCachedResult(Guid computerId, string description, ulong hash64, out LlmCheckResult result)
    {
        lock (_recentChecks)
        {
            if (_recentChecks.TryGetValue(computerId, out var entries))
            {
                var cached = entries.FirstOrDefault(entry =>
                    entry.Description == description
                    && entry.ScreenshotHash64 == hash64
                    && !entry.Result.IsError);

                if (cached is not null)
                {
                    result = cached.Result;
                    return true;
                }
            }
        }

        result = null!;
        return false;
    }

    private void StoreCachedResult(
        Guid computerId,
        string description,
        ulong screenshotHash64,
        DateTime screenshotTimestamp,
        LlmCheckResult result)
    {
        lock (_recentChecks)
        {
            if (!_recentChecks.TryGetValue(computerId, out var entries))
            {
                entries = [];
                _recentChecks[computerId] = entries;
            }

            entries.RemoveAll(entry =>
                entry.Description == description
                && entry.ScreenshotHash64 == screenshotHash64);

            entries.Insert(0, new CachedLlmCheckEntry(
                description,
                screenshotHash64,
                screenshotTimestamp,
                result));

            if (entries.Count > MaxCachedChecksPerComputer)
                entries.RemoveRange(MaxCachedChecksPerComputer, entries.Count - MaxCachedChecksPerComputer);
        }
    }

    private void ClearCache()
    {
        lock (_recentChecks)
            _recentChecks.Clear();
    }

    internal static string BuildTimeoutMessage() =>
        string.Format(TimeoutMessageTemplate, DefaultPerComputerTimeoutSeconds);

    private sealed record CachedLlmCheckEntry(
        string Description,
        ulong ScreenshotHash64,
        DateTime ScreenshotTimestamp,
        LlmCheckResult Result);
}
