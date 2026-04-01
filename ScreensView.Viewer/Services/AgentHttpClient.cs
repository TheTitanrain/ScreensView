using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ScreensView.Shared;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public class AgentHttpClient : IDisposable
{
    private readonly Action<ComputerConfig, string>? _onThumbprintPinned;

    public AgentHttpClient(Action<ComputerConfig, string>? onThumbprintPinned = null)
    {
        _onThumbprintPinned = onThumbprintPinned;
    }

    public async Task<ScreenshotResponse?> GetScreenshotAsync(ComputerConfig computer, CancellationToken ct = default)
    {
        using var handler = BuildHandler(computer);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var url = $"https://{computer.Host}:{computer.Port}/screenshot";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Constants.ApiKeyHeader, computer.ApiKey);
        var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SessionLockedException(ExtractMessage(body));
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScreenshotResponse>(ct);
    }

    public async Task<bool> CheckHealthAsync(ComputerConfig computer, CancellationToken ct = default)
    {
        try
        {
            using var handler = BuildHandler(computer);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://{computer.Host}:{computer.Port}/health";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(Constants.ApiKeyHeader, computer.ApiKey);
            var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private HttpClientHandler BuildHandler(ComputerConfig computer)
    {
        var handler = new HttpClientHandler();

        if (string.IsNullOrEmpty(computer.CertThumbprint))
        {
            // First connection — trust and pin
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert == null) return false;
                var thumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                _onThumbprintPinned?.Invoke(computer, thumbprint);
                return true;
            };
        }
        else
        {
            // Subsequent connections — validate against pinned thumbprint
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert == null) return false;
                var thumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                return string.Equals(thumbprint, computer.CertThumbprint, StringComparison.OrdinalIgnoreCase);
            };
        }

        return handler;
    }

    // Extracts the human-readable message from either a RFC 7807 ProblemDetails JSON body
    // (modern agent: Results.Problem) or a plain-text body (legacy agent).
    private static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
                return detail.GetString() ?? body;
        }
        catch (JsonException) { }
        return body;
    }

    public void Dispose() { }
}
