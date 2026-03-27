using System.Collections.ObjectModel;
using ScreensView.Viewer.ViewModels;

namespace ScreensView.Viewer.Services;

public class ScreenshotPollerService : IDisposable
{
    private readonly AgentHttpClient _http;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    public ScreenshotPollerService(AgentHttpClient http)
    {
        _http = http;
    }

    public void Start(ObservableCollection<ComputerViewModel> computers, int intervalSeconds)
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

    private async Task RunAsync(ObservableCollection<ComputerViewModel> computers, int intervalSeconds, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var tasks = computers
                .Where(c => c.IsEnabled)
                .Select(c => PollComputerAsync(c, ct));

            await Task.WhenAll(tasks);

            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task PollComputerAsync(ComputerViewModel vm, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetScreenshotAsync(vm.ToConfig(), ct);
            if (response != null)
                System.Windows.Application.Current.Dispatcher.Invoke(() => vm.UpdateScreenshot(response));
        }
        catch (SessionLockedException ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => vm.SetLocked(ex.Message));
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => vm.SetError(ex.Message));
        }
    }

    public void Dispose() => Stop();
}
