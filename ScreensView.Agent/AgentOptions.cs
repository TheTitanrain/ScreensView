namespace ScreensView.Agent;

public class AgentOptions
{
    public int Port { get; set; } = 5443;
    public string ApiKey { get; set; } = string.Empty;
    public int ScreenshotQuality { get; set; } = 75;
}
