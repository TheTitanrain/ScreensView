using System.Runtime.ExceptionServices;
using System.Windows.Documents;
using ScreensView.Viewer.Views;

namespace ScreensView.Tests;

public sealed class AboutWindowTests
{
    [Fact]
    public void GitHubLink_PointsToCanonicalRepository()
    {
        var snapshot = RunOnSta(() =>
        {
            var window = new AboutWindow();
            var link = Assert.IsType<Hyperlink>(window.FindName("GitHubLink"));

            return new
            {
                Url = link.NavigateUri?.AbsoluteUri,
                Text = string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)).Trim()
            };
        });

        Assert.Equal("https://github.com/TheTitanrain/ScreensView", snapshot.Url);
        Assert.Equal("github.com/TheTitanrain/ScreensView", snapshot.Text);
    }

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            ExceptionDispatchInfo.Capture(caught).Throw();

        return result!;
    }
}
