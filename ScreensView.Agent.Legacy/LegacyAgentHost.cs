using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreensView.Shared;
using ScreensView.Shared.Models;

namespace ScreensView.Agent.Legacy;

internal sealed class LegacyAgentHost : IDisposable
{
    private readonly AgentOptions _options;
    private readonly ScreenshotService _screenshotService;
    private readonly HttpListener _listener = new HttpListener();
    private readonly System.Collections.Concurrent.ConcurrentBag<Task> _requestTasks = new();
    private CancellationTokenSource? _cancellation;
    private Task? _listenLoop;

    public LegacyAgentHost(AgentOptions options)
    {
        _options = options;
        _screenshotService = new ScreenshotService(options);
    }

    public void Start()
    {
        var certificate = new CertificateService().GetOrCreateCertificate();
        new HttpsBindingManager().EnsureBinding(_options.Port, certificate);

        _listener.Prefixes.Add($"https://+:{_options.Port}/");
        _listener.Start();

        _cancellation = new CancellationTokenSource();
        _listenLoop = Task.Run(() => ListenLoopAsync(_cancellation.Token));
    }

    public void Dispose()
    {
        if (_cancellation != null)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }

        if (_listener.IsListening)
            _listener.Stop();

        try
        {
            _listenLoop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        Task.WaitAll(_requestTasks.ToArray(), TimeSpan.FromSeconds(10));

        _listener.Close();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Don't pass cancellationToken — don't cancel in-flight requests mid-handling
            _requestTasks.Add(Task.Run(() => HandleRequest(context)));
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            if (!IsAuthorized(context.Request))
            {
                WriteText(context.Response, 401, "Unauthorized");
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                WriteText(context.Response, 405, "Method Not Allowed");
                return;
            }

            switch (context.Request.Url?.AbsolutePath)
            {
                case "/health":
                    WriteJson(context.Response, 200, AgentJsonSerializer.SerializeHealthResponse(Environment.MachineName));
                    return;
                case "/screenshot":
                    try
                    {
                        var jpeg = _screenshotService.CaptureJpeg();
                        var response = new ScreenshotResponse
                        {
                            ImageBase64 = Convert.ToBase64String(jpeg),
                            Timestamp = DateTime.UtcNow,
                            MachineName = Environment.MachineName
                        };
                        WriteJson(context.Response, 200, AgentJsonSerializer.SerializeScreenshotResponse(response));
                    }
                    catch (NoActiveSessionException ex)
                    {
                        WriteText(context.Response, 503, ex.Message);
                    }
                    return;
                default:
                    WriteText(context.Response, 404, "Not Found");
                    return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.EventLog.WriteEntry(
                "ScreensViewAgent", ex.ToString(), System.Diagnostics.EventLogEntryType.Error);
            WriteText(context.Response, 500, "Internal server error");
        }
        finally
        {
            context.Response.Close();
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers[Constants.ApiKeyHeader];
        return FixedTimeEquals(header, _options.ApiKey);
    }

    // Constant-time comparison to prevent timing attacks.
    // .NET Framework 4.8 does not have CryptographicOperations.FixedTimeEquals.
    private static bool FixedTimeEquals(string? a, string b)
    {
        if (a == null) return false;
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        int diff = 0;
        for (int i = 0; i < aBytes.Length; i++)
            diff |= aBytes[i] ^ bBytes[i];
        return diff == 0;
    }

    private static void WriteJson(HttpListenerResponse response, int statusCode, string json)
    {
        var data = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = data.LongLength;
        response.OutputStream.Write(data, 0, data.Length);
    }

    private static void WriteText(HttpListenerResponse response, int statusCode, string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = data.LongLength;
        response.OutputStream.Write(data, 0, data.Length);
    }
}
