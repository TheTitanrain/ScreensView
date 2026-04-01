namespace ScreensView.Viewer.Helpers
{
    internal static class WindowWidthHelper
    {
        internal static double ComputeMinWidth(
            double panelWidth,
            double contentWidth,
            double windowWidth,
            double workAreaWidth)
        {
            double frameWidth = windowWidth - contentWidth;
            double target = panelWidth + frameWidth;
            return Math.Min(target, workAreaWidth);
        }
    }
}
