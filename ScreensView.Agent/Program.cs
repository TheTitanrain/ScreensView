using ScreensView.Agent;
using ScreensView.Shared.Models;

if (args is ["--screenshot-helper", var pipe, var qualStr])
{
    ScreenshotHelper.Run(pipe, int.TryParse(qualStr, out var q) ? q : 75);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

var agentOptions = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
if (string.IsNullOrEmpty(agentOptions.ApiKey))
    throw new InvalidOperationException("Agent:ApiKey is not configured in appsettings.json");

builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton<CertificateService>();
builder.Services.AddSingleton<ScreenshotService>();

builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    var sp = serverOptions.ApplicationServices;
    var certService = sp.GetRequiredService<CertificateService>();
    var cert = certService.GetOrCreateCertificate();
    serverOptions.ListenAnyIP(agentOptions.Port, listenOptions =>
    {
        listenOptions.UseHttps(cert);
    });
});

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/screenshot", (ScreenshotService screenshotService) =>
{
    try
    {
        var jpeg = screenshotService.CaptureJpeg();
        var response = new ScreenshotResponse
        {
            ImageBase64 = Convert.ToBase64String(jpeg),
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        return Results.Ok(response);
    }
    catch (NoActiveSessionException ex)
    {
        return Results.Problem(ex.Message, statusCode: 503);
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", machine = Environment.MachineName }));

app.Run();
