using ScreensView.Viewer.Services;

namespace ScreensView.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void Get_WhenNoApplicationCurrent_UsesCurrentLanguageResources()
    {
        var previousLanguage = LocalizationService.CurrentLanguage;
        LocalizationService.Switch("en");

        try
        {
            var value = LocalizationService.Get("Str.Install.TitleDotNet");

            Assert.Equal("Install .NET 8 runtimes", value);
        }
        finally
        {
            LocalizationService.Switch(previousLanguage);
        }
    }
}
