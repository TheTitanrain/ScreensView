using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace ScreensView.Viewer.Services;

public interface ILlamaServerProcessService : IDisposable
{
    bool IsRunning { get; }
    Task<string> StartAsync(string exePath, string modelPath, string projectorPath, CancellationToken ct);
    Task StopAsync();
}

public class LlamaServerProcessService : ILlamaServerProcessService
{
    private readonly IViewerLogService _log;
    private readonly HttpClient _http;
    private Process? _process;
    private string? _baseUrl;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int StartupTimeoutSeconds = 120;
    private const int HealthPollIntervalMs = 500;

    public LlamaServerProcessService() : this(null) { }

    internal LlamaServerProcessService(IViewerLogService? log)
    {
        _log = log ?? new NullViewerLogService();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    }

    public bool IsRunning => _process is { HasExited: false };

    public async Task<string> StartAsync(
        string exePath, string modelPath, string projectorPath, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                await StopCoreAsync().ConfigureAwait(false);

            KillStaleProcesses();

            var port = FindFreePort();
            _baseUrl = $"http://127.0.0.1:{port}";

            _log.LogInfo("LlamaServer.Start",
                $"Starting llama-server on port {port}. Model='{modelPath}'.");

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = BuildArgs(modelPath, projectorPath, port)
            };

            _process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить llama-server.exe.");

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    _log.LogInfo("LlamaServer.Stdout", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    _log.LogInfo("LlamaServer.Stderr", e.Data);
            };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await WaitForReadyAsync(port, ct).ConfigureAwait(false);

            _log.LogInfo("LlamaServer.Ready", $"llama-server ready at {_baseUrl}.");
            return _baseUrl;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _process?.Kill(entireProcessTree: true);
        _process?.Dispose();
        _http.Dispose();
        _lock.Dispose();
    }

    private async Task StopCoreAsync()
    {
        if (_process is null)
            return;

        _log.LogInfo("LlamaServer.Stop", "Stopping llama-server.");
        try
        {
            _process.Kill(entireProcessTree: true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning("LlamaServer.StopWarning", $"Error stopping process: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _baseUrl = null;
        }
    }

    private async Task WaitForReadyAsync(int port, CancellationToken ct)
    {
        var url = $"http://127.0.0.1:{port}/health";
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(StartupTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        while (!linked.Token.IsCancellationRequested)
        {
            if (_process?.HasExited == true)
                throw new InvalidOperationException(
                    "llama-server завершился неожиданно при запуске. " +
                    "Проверьте путь к модели и логи.");

            try
            {
                using var response = await _http.GetAsync(url, linked.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                    if (body.Contains("\"ok\"", StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"llama-server не ответил за {StartupTimeoutSeconds} секунд. " +
                    "Возможно, модель слишком большая для доступной памяти.");
            }
            catch
            {
                // server not ready yet — keep polling
            }

            await Task.Delay(HealthPollIntervalMs, linked.Token).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
    }

    private static string BuildArgs(string modelPath, string projectorPath, int port)
    {
        var threads = Math.Max(1, Environment.ProcessorCount);
        return $"--model \"{modelPath}\" --mmproj \"{projectorPath}\" " +
               $"--port {port} --host 127.0.0.1 --threads {threads} --reasoning-budget 0";
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void KillStaleProcesses()
    {
        foreach (var p in Process.GetProcessesByName("llama-server"))
        {
            try { p.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
            finally { p.Dispose(); }
        }
    }
}
