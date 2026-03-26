namespace ScreensView.Shared.Models;

public class ScreenshotResponse
{
    public string ImageBase64 { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string MachineName { get; set; } = string.Empty;
}
