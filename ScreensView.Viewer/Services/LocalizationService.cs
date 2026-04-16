using System.Windows;

namespace ScreensView.Viewer.Services;

/// <summary>
/// Hot-swaps the active language ResourceDictionary in Application.Resources.
/// Provides typed string access for C# code (message boxes, validation, etc.).
/// </summary>
public static class LocalizationService
{
    private const string RuUri = "pack://application:,,,/Resources/Strings.ru.xaml";
    private const string EnUri = "pack://application:,,,/Resources/Strings.en.xaml";

    public static string CurrentLanguage { get; private set; } = "ru";

    /// <summary>Applies language once at startup before any window opens.</summary>
    public static void Apply(string languageCode)
    {
        CurrentLanguage = ResolveLanguage(languageCode);
        SwapDictionary(CurrentLanguage == "en" ? EnUri : RuUri);
    }

    private static string ResolveLanguage(string code)
    {
        if (code == "en") return "en";
        if (code == "ru") return "ru";
        // "auto": detect from OS locale
        return System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru"
            ? "ru" : "en";
    }

    /// <summary>Hot-swaps language at runtime. All {DynamicResource} bindings update automatically.</summary>
    public static void Switch(string languageCode) => Apply(languageCode);

    /// <summary>Returns the localized string for a resource key. Falls back to the key itself.</summary>
    public static string Get(string key)
        => Application.Current.TryFindResource(key) as string ?? key;

    private static void SwapDictionary(string newUri)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(IsLanguageDictionary);
        if (existing != null)
            merged.Remove(existing);
        merged.Add(new ResourceDictionary { Source = new Uri(newUri, UriKind.Absolute) });
    }

    private static bool IsLanguageDictionary(ResourceDictionary d)
    {
        var src = d.Source?.OriginalString ?? string.Empty;
        return src.Contains("Strings.ru.xaml") || src.Contains("Strings.en.xaml");
    }
}
