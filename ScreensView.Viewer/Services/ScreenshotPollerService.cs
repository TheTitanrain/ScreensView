using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Services;

public interface IScreenshotPollerService : IDisposable
{
    Task RefreshNowAsync(IEnumerable<ComputerViewModel> computers);
    void Start(IEnumerable<ComputerViewModel> computers, int intervalSeconds);
    void Stop();
}

public class ScreenshotPollerService : IScreenshotPollerService
{
    private readonly AgentHttpClient _http;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    public ScreenshotPollerService(AgentHttpClient http, IUiDispatcher dispatcher)
    {
        _http = http;
        _dispatcher = dispatcher;
    }

    public void Start(IEnumerable<ComputerViewModel> computers, int intervalSeconds)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _pollingTask = RunAsync(computers, intervalSeconds, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public Task RefreshNowAsync(IEnumerable<ComputerViewModel> computers)
    {
        return PollBatchAsync(computers, CancellationToken.None);
    }

    private async Task RunAsync(IEnumerable<ComputerViewModel> computers, int intervalSeconds, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollBatchAsync(computers, ct);

            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task PollBatchAsync(IEnumerable<ComputerViewModel> computers, CancellationToken ct)
    {
        await _pollGate.WaitAsync(ct);
        try
        {
            var snapshot = _dispatcher.Invoke(() => computers.ToList());
            var tasks = snapshot
                .Where(c => c.IsEnabled)
                .Select(c => PollComputerAsync(c, ct))
                .ToList();

            await Task.WhenAll(tasks);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task PollComputerAsync(ComputerViewModel vm, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetScreenshotAsync(vm.ToConfig(), ct);
            if (response != null)
                _dispatcher.Invoke(() => vm.UpdateScreenshot(response));
        }
        catch (SessionLockedException ex)
        {
            _dispatcher.Invoke(() => vm.SetLocked(ex.Message));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ScreenshotPollerService] PollComputerAsync {vm.Name} — {ex.GetType().Name}: {ex}");
            _dispatcher.Invoke(() => vm.SetError(ex.Message));
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
