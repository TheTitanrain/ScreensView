namespace ScreensView.Viewer.Helpers;

internal static class ComputerListHelpers
{
    public static string FormatNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        if (list.Count <= 10)
            return string.Join(", ", list);
        return string.Join(", ", list.Take(10)) + $" и ещё {list.Count - 10}";
    }
}
