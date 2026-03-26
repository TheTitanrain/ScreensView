using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using ScreensView.Shared;
using ScreensView.Shared.Models;

namespace ScreensView.Viewer.Services;

public class AgentHttpClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly Action<ComputerConfig, string>? _onThumbprintPinned;

    public AgentHttpClient(Action<ComputerConfig, string>? onThumbprintPinned = null)
    {
        _onThumbprintPinned = onThumbprintPinned;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true  // overridden per-request
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<ScreenshotResponse?> GetScreenshotAsync(ComputerConfig computer, CancellationToken ct = default)
    {
        using var handler = BuildHandler(computer);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var url = $"https://{computer.Host}:{computer.Port}/screenshot";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(Constants.ApiKeyHeader, computer.ApiKey);
        var response = await client.SendAsync(request, ct);
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

    public void Dispose() => _client.Dispose();
}
