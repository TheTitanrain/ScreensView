using System.IO;
using System.Text;

namespace ScreensView.Viewer.Services;

public interface IViewerLogService
{
    void LogInfo(string eventName, string message);
    void LogWarning(string eventName, string message);
    void LogError(string eventName, string message, Exception? exception = null);
}

public sealed class ViewerLogService : IViewerLogService
{
    private const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;

    private readonly string _logsDirectory;
    private readonly string _logFilePath;
    private readonly string _rotatedLogFilePath;
    private readonly long _maxFileSizeBytes;
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();

    public ViewerLogService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreensView"))
    {
    }

    internal ViewerLogService(string basePath, long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        _logsDirectory = Path.Combine(basePath, "logs");
        _logFilePath = Path.Combine(_logsDirectory, "viewer.log");
        _rotatedLogFilePath = Path.Combine(_logsDirectory, "viewer.log.1");
        _maxFileSizeBytes = maxFileSizeBytes;
        Directory.CreateDirectory(_logsDirectory);
    }

    public void LogInfo(string eventName, string message) => Write("INFO", eventName, message);

    public void LogWarning(string eventName, string message) => Write("WARN", eventName, message);

    public void LogError(string eventName, string message, Exception? exception = null)
        => Write("ERROR", eventName, message, exception);

    private void Write(string level, string eventName, string message, Exception? exception = null)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_logsDirectory);

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" | ");
            builder.Append(level);
            builder.Append(" | ");
            builder.Append(eventName);
            builder.Append(" | ");
            builder.AppendLine(message);

            if (exception is not null)
                AppendException(builder, exception);

            RotateIfNeeded(_encoding.GetByteCount(builder.ToString()));
            File.AppendAllText(_logFilePath, builder.ToString(), _encoding);
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_logFilePath))
            return;

        var currentSize = new FileInfo(_logFilePath).Length;
        if (currentSize + incomingBytes <= _maxFileSizeBytes)
            return;

        if (File.Exists(_rotatedLogFilePath))
            File.Delete(_rotatedLogFilePath);

        File.Move(_logFilePath, _rotatedLogFilePath);
    }

    private static void AppendException(StringBuilder builder, Exception exception)
    {
        var current = exception;
        var depth = 0;
        while (current is not null)
        {
            var label = depth == 0 ? "Exception" : $"InnerException[{depth}]";
            builder.Append(label);
            builder.Append(": ");
            builder.AppendLine(current.GetType().FullName);
            builder.Append("Message: ");
            builder.AppendLine(current.Message);

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                builder.AppendLine("StackTrace:");
                builder.AppendLine(current.StackTrace);
            }

            current = current.InnerException;
            depth++;
        }
    }
}

internal sealed class NullViewerLogService : IViewerLogService
{
    public void LogInfo(string eventName, string message)
    {
    }

    public void LogWarning(string eventName, string message)
    {
    }

    public void LogError(string eventName, string message, Exception? exception = null)
    {
    }
}
