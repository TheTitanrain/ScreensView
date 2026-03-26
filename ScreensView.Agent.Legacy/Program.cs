using System.ServiceProcess;
using ScreensView.Shared;

namespace ScreensView.Agent.Legacy;

internal static class Program
{
    private static void Main()
    {
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
