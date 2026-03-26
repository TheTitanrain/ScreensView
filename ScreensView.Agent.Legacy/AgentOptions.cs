using System.Runtime.Serialization;

namespace ScreensView.Agent.Legacy;

[DataContract]
internal sealed class AgentOptions
{
    [DataMember(Name = "Port")]
    public int Port { get; set; } = 5443;

    [DataMember(Name = "ApiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [DataMember(Name = "ScreenshotQuality")]
    public int ScreenshotQuality { get; set; } = 75;
}
