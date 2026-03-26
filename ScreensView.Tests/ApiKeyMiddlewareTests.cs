using Microsoft.AspNetCore.Http;
using ScreensView.Agent;
using ScreensView.Shared;

namespace ScreensView.Tests;

public class ApiKeyMiddlewareTests
{
    private const string CorrectKey = "test-api-key-abc123";

    private static ApiKeyMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, new AgentOptions { ApiKey = CorrectKey });

    private static DefaultHttpContext CreateContext(string? apiKey = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        if (apiKey != null)
            ctx.Request.Headers[Constants.ApiKeyHeader] = apiKey;
        return ctx;
    }

    [Fact]
    public async Task MissingApiKeyHeader_Returns401()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task WrongApiKey_Returns401()
    {
        var context = CreateContext("wrong-key");
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task CorrectApiKey_CallsNext()
    {
        var context = CreateContext(CorrectKey);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CorrectApiKey_DoesNotReturn401()
    {
        var context = CreateContext(CorrectKey);
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task EmptyApiKey_Returns401()
    {
        var context = CreateContext(string.Empty);
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
