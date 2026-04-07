using LLama.Native;

namespace ScreensView.Viewer.Services;

internal static class LlamaNativeDiagnostics
{
    private const int MaxEntries = 256;

    private static readonly object Sync = new();
    private static readonly Queue<NativeLogEntry> Entries = [];
    private static IViewerLogService _log = new NullViewerLogService();
    private static long _sequence;
    private static bool _configured;

    public static void Configure(IViewerLogService log)
    {
        lock (Sync)
        {
            _log = log;
            if (_configured)
                return;

            NativeLibraryConfig.All.WithLogCallback(HandleLog);
            _configured = true;
        }
    }

    public static long CaptureCursor() => Interlocked.Read(ref _sequence);

    public static string? GetRelevantMessagesSince(long cursor)
    {
        lock (Sync)
        {
            var recent = Entries
                .Where(entry => entry.Sequence > cursor && entry.Level is LLamaLogLevel.Error or LLamaLogLevel.Warning)
                .TakeLast(5)
                .Select(entry => $"{entry.Level}: {entry.Message}")
                .ToArray();

            return recent.Length == 0 ? null : string.Join(" | ", recent);
        }
    }

    private static void HandleLog(LLamaLogLevel level, string message)
    {
        var normalized = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        IViewerLogService log;
        lock (Sync)
        {
            var sequence = Interlocked.Increment(ref _sequence);
            Entries.Enqueue(new NativeLogEntry(sequence, level, normalized));
            while (Entries.Count > MaxEntries)
                Entries.Dequeue();

            log = _log;
        }

        var eventName = $"Llm.Native.{level}";
        switch (level)
        {
            case LLamaLogLevel.Error:
                log.LogError(eventName, normalized);
                break;
            case LLamaLogLevel.Warning:
                log.LogWarning(eventName, normalized);
                break;
        }
    }

    private sealed record NativeLogEntry(long Sequence, LLamaLogLevel Level, string Message);
}
