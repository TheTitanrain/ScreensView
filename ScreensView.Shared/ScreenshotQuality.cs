namespace ScreensView.Shared;

public static class ScreenshotQuality
{
    public const int Default = 75;

    public static int ParseOrDefault(string? raw)
    {
        if (!int.TryParse(raw, out var parsed))
            return Default;

        if (parsed < 0)
            return 0;

        return parsed > 100 ? 100 : parsed;
    }
}
