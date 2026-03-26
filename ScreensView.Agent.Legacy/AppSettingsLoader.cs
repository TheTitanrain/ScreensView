using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ScreensView.Agent.Legacy;

[DataContract]
internal sealed class AgentSettingsFile
{
    [DataMember(Name = "Agent")]
    public AgentOptions Agent { get; set; } = new AgentOptions();
}

internal static class AppSettingsLoader
{
    public static AgentOptions Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"appsettings.json was not found at '{path}'.", path);

        using var stream = File.OpenRead(path);
        var serializer = new DataContractJsonSerializer(typeof(AgentSettingsFile));
        var settings = serializer.ReadObject(stream) as AgentSettingsFile ?? new AgentSettingsFile();

        if (string.IsNullOrWhiteSpace(settings.Agent.ApiKey))
            throw new InvalidOperationException("Agent:ApiKey is not configured in appsettings.json");

        return settings.Agent;
    }
}
