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

    [Fact]
    public void Switch_FiresLanguageChangedEvent()
    {
        var previousLanguage = LocalizationService.CurrentLanguage;
        int callCount = 0;
        Action handler = () => callCount++;
        LocalizationService.LanguageChanged += handler;

        try
        {
            LocalizationService.Switch("en");

            Assert.Equal(1, callCount);
        }
        finally
        {
            LocalizationService.LanguageChanged -= handler;
            LocalizationService.Switch(previousLanguage);
        }
    }
}
