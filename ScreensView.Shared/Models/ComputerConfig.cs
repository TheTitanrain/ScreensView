namespace ScreensView.Shared.Models;

public class ComputerConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = Constants.DefaultPort;
    public string ApiKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    /// <summary>SHA-256 thumbprint of the agent's self-signed cert (pinned on first connection).</summary>
    public string CertThumbprint { get; set; } = string.Empty;
    public string? Description { get; set; }
}
