using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class ViewerLogServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public ViewerLogServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LogInfo_CreatesLogFileInLogsFolder()
    {
        var sut = new ViewerLogService(_tempDir);

        sut.LogInfo("Llm.Start", "Starting validation");

        var logPath = Path.Combine(_tempDir, "logs", "viewer.log");
        Assert.True(File.Exists(logPath));
        var contents = File.ReadAllText(logPath);
        Assert.Contains("INFO", contents);
        Assert.Contains("Llm.Start", contents);
        Assert.Contains("Starting validation", contents);
    }

    [Fact]
    public void LogError_WritesExceptionDetails()
    {
        var sut = new ViewerLogService(_tempDir);
        var exception = new InvalidOperationException("outer boom", new Exception("inner boom"));

        sut.LogError("Llm.ValidateFailed", "Validation failed", exception);

        var logPath = Path.Combine(_tempDir, "logs", "viewer.log");
        var contents = File.ReadAllText(logPath);
        Assert.Contains("ERROR", contents);
        Assert.Contains("Validation failed", contents);
        Assert.Contains("InvalidOperationException", contents);
        Assert.Contains("outer boom", contents);
        Assert.Contains("inner boom", contents);
    }

    [Fact]
    public void LogInfo_WhenFileExceedsLimit_RotatesLog()
    {
        var sut = new ViewerLogService(_tempDir, maxFileSizeBytes: 120);

        sut.LogInfo("Llm.Step1", new string('a', 90));
        sut.LogInfo("Llm.Step2", new string('b', 90));

        var currentLog = Path.Combine(_tempDir, "logs", "viewer.log");
        var rotatedLog = Path.Combine(_tempDir, "logs", "viewer.log.1");

        Assert.True(File.Exists(currentLog));
        Assert.True(File.Exists(rotatedLog));
        Assert.Contains("Llm.Step2", File.ReadAllText(currentLog));
        Assert.Contains("Llm.Step1", File.ReadAllText(rotatedLog));
    }
}
