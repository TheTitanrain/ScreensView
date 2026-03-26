using System.Text.Json;
using ScreensView.Shared;
using ScreensView.Shared.Models;

namespace ScreensView.Tests;

public sealed class AgentJsonSerializerTests
{
    [Fact]
    public void SerializeScreenshotResponse_ProducesViewerCompatibleJson()
    {
        var timestamp = new DateTime(2026, 3, 26, 12, 34, 56, DateTimeKind.Utc);
        var response = new ScreenshotResponse
        {
            ImageBase64 = Convert.ToBase64String([1, 2, 3, 4]),
            Timestamp = timestamp,
            MachineName = "pc-01"
        };

        var json = AgentJsonSerializer.SerializeScreenshotResponse(response);
        var parsed = JsonSerializer.Deserialize<ScreenshotResponse>(json);

        Assert.NotNull(parsed);
        Assert.Equal(response.ImageBase64, parsed.ImageBase64);
        Assert.Equal(response.Timestamp, parsed.Timestamp);
        Assert.Equal(response.MachineName, parsed.MachineName);
    }

    [Fact]
    public void SerializeScreenshotResponse_EscapesSpecialCharactersInMachineName()
    {
        var response = new ScreenshotResponse
        {
            ImageBase64 = "abc",
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            MachineName = "pc\"with\\back\nslash"
        };

        var json = AgentJsonSerializer.SerializeScreenshotResponse(response);
        var parsed = JsonSerializer.Deserialize<ScreenshotResponse>(json);

        Assert.NotNull(parsed);
        Assert.Equal(response.MachineName, parsed.MachineName);
    }

    [Fact]
    public void SerializeHealthResponse_ProducesExpectedPayload()
    {
        var json = AgentJsonSerializer.SerializeHealthResponse("pc-01");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("pc-01", doc.RootElement.GetProperty("machine").GetString());
    }
}
