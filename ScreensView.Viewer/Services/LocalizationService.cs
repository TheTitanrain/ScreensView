using System.Windows;

namespace ScreensView.Viewer.Services;

/// <summary>
/// Hot-swaps the active language ResourceDictionary in Application.Resources.
/// Provides typed string access for C# code (message boxes, validation, etc.).
/// </summary>
public static class LocalizationService
{
    private const string RuUri = "/ScreensView.Viewer;component/Resources/Strings.ru.xaml";
    private const string EnUri = "/ScreensView.Viewer;component/Resources/Strings.en.xaml";
    private static readonly object Sync = new();
    private static ResourceDictionary? _fallbackDictionary;

    public static string CurrentLanguage { get; private set; } = "ru";

    /// <summary>Applies language once at startup before any window opens.</summary>
    public static void Apply(string languageCode)
    {
        CurrentLanguage = ResolveLanguage(languageCode);
        lock (Sync)
            _fallbackDictionary = LoadDictionaryForCurrentLanguage();

        var app = Application.Current;
        if (app is null)
            return;

        SwapDictionary(app, GetDictionaryUri(CurrentLanguage));
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
    {
        if (Application.Current?.TryFindResource(key) is string value)
            return value;

        lock (Sync)
        {
            _fallbackDictionary ??= LoadDictionaryForCurrentLanguage();
            return _fallbackDictionary.Contains(key)
                ? _fallbackDictionary[key] as string ?? key
                : key;
        }
    }

    private static void SwapDictionary(Application application, Uri newUri)
    {
        var merged = application.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(IsLanguageDictionary);
        if (existing != null)
            merged.Remove(existing);
        merged.Add(new ResourceDictionary { Source = newUri });
    }

    private static ResourceDictionary LoadDictionaryForCurrentLanguage() =>
        new()
        {
            Source = GetDictionaryUri(CurrentLanguage)
        };

    private static Uri GetDictionaryUri(string languageCode) =>
        new(languageCode == "en" ? EnUri : RuUri, UriKind.Relative);

    private static bool IsLanguageDictionary(ResourceDictionary d)
    {
        var src = d.Source?.OriginalString ?? string.Empty;
        return src.Contains("Strings.ru.xaml") || src.Contains("Strings.en.xaml");
    }
}
