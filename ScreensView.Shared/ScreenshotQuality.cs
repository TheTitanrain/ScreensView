namespace ScreensView.Shared;

public static class ScreenshotQuality
{
    public const int Default = 75;
    public const int Min = 0;
    public const int Max = 100;

    public static int ParseOrDefault(string? raw)
    {
        if (!int.TryParse(raw, out var parsed))
            return Default;

        if (parsed < Min)
            return Min;

        return parsed > Max ? Max : parsed;
    }
}
