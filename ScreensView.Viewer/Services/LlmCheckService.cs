using ScreensView.Viewer.Models;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Services;

public interface ILlmCheckService
{
    void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes);
    void UpdateInterval(int intervalMinutes);
    void Stop();
}

public class LlmCheckService : ILlmCheckService
{
    private readonly ILlmInferenceService _inference;
    private volatile int _intervalMinutes = 5;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private IReadOnlyList<ComputerViewModel>? _computers;

    public LlmCheckService(ILlmInferenceService inference)
    {
        _inference = inference;
    }

    public void Start(IReadOnlyList<ComputerViewModel> computers, int intervalMinutes)
    {
        if (_cts is not null)
            return; // idempotent — already running

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
        if (_computers is not null)
            SetServiceActive(_computers, isActive: false);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunCycleAsync(_computers ?? [], ct);

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

    // Internal for testing — allows direct cycle execution without the delay
    internal async Task RunCycleAsync(IReadOnlyList<ComputerViewModel> computers,
        CancellationToken ct = default)
    {
        var snapshot = System.Windows.Application.Current is not null
            ? System.Windows.Application.Current.Dispatcher.Invoke(
                () => computers
                    .Where(vm => vm.IsEnabled && !string.IsNullOrEmpty(vm.Description))
                    .ToList())
            : computers
                .Where(vm => vm.IsEnabled && !string.IsNullOrEmpty(vm.Description))
                .ToList(); // test path: no Application.Current

        foreach (var vm in snapshot)
        {
            var screenshotCopy = vm.Screenshot;
            if (screenshotCopy is null)
                continue;

            var descriptionAtStart = vm.Description;
            if (string.IsNullOrEmpty(descriptionAtStart))
                continue;

            SetOnDispatcher(() => vm.IsLlmChecking = true);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                var result = await _inference.AnalyzeAsync(screenshotCopy, descriptionAtStart, linked.Token);

                SetOnDispatcher(() =>
                {
                    if (vm.Description == descriptionAtStart)
                        vm.LastLlmCheck = result;
                });
            }
            catch (Exception ex)
            {
                SetOnDispatcher(() =>
                {
                    if (vm.Description == descriptionAtStart)
                        vm.LastLlmCheck = new LlmCheckResult(false, ex.Message, IsError: true, DateTime.Now);
                });
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
}
