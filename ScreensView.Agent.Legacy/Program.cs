using System.ServiceProcess;
using ScreensView.Shared;

namespace ScreensView.Agent.Legacy;

internal static class Program
{
    private static void Main(string[] args)
    {
        var idx = Array.IndexOf(args, "--screenshot-helper");
        if (idx >= 0 && idx + 2 < args.Length)
        {
            ScreenshotHelper.Run(args[idx + 1], int.TryParse(args[idx + 2], out var q) ? q : 75);
            return;
        }

        ServiceBase.Run(new AgentWindowsService());
    }
}

internal sealed class AgentWindowsService : ServiceBase
{
    private LegacyAgentHost? _host;

    public AgentWindowsService()
    {
        ServiceName = Constants.ServiceName;
        CanStop = true;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        var options = AppSettingsLoader.Load(AppDomain.CurrentDomain.BaseDirectory);
        _host = new LegacyAgentHost(options);
        _host.Start();
    }

    protected override void OnStop()
    {
        _host?.Dispose();
        _host = null;
    }
}
