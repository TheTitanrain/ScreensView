using System.Security.Cryptography;
using System.Text;
using ScreensView.Shared;

namespace ScreensView.Agent;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, AgentOptions options)
    {
        _next = next;
        _apiKey = options.ApiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(Constants.ApiKeyHeader, out var key) ||
            !FixedTimeEquals((string?)key, _apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
        await _next(context);
    }

    private static bool FixedTimeEquals(string? a, string b)
    {
        if (a == null) return false;
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
