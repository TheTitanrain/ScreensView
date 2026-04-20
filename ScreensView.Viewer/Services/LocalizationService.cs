using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Xml;

namespace ScreensView.Viewer.Services;

/// <summary>
/// Hot-swaps the active language ResourceDictionary in Application.Resources.
/// Provides typed string access for C# code (message boxes, validation, etc.).
/// Also supports test and headless paths where no WPF Application exists.
/// </summary>
public static class LocalizationService
{
    private const string RuUri = "pack://application:,,,/Resources/Strings.ru.xaml";
    private const string EnUri = "pack://application:,,,/Resources/Strings.en.xaml";
    private static readonly Dictionary<string, ResourceDictionary> CachedDictionaries = [];

    public static string CurrentLanguage { get; private set; } = "ru";

    public static event Action? LanguageChanged;

    /// <summary>Applies language once at startup before any window opens.</summary>
    public static void Apply(string languageCode)
    {
        CurrentLanguage = ResolveLanguage(languageCode);
        SwapDictionary(CurrentLanguage == "en" ? EnUri : RuUri);
        LanguageChanged?.Invoke();
    }

    private static string ResolveLanguage(string code)
    {
        if (code == "en") return "en";
        if (code == "ru") return "ru";
        return System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru"
            ? "ru" : "en";
    }

    /// <summary>Hot-swaps language at runtime. All {DynamicResource} bindings update automatically.</summary>
    public static void Switch(string languageCode) => Apply(languageCode);

    /// <summary>Returns the localized string for a resource key. Falls back to the key itself.</summary>
    public static string Get(string key)
    {
        if (GetFallbackDictionary()[key] is string fallbackValue)
            return fallbackValue;

        var application = Application.Current;
        if (application?.TryFindResource(key) is string value)
            return value;

        return key;
    }

    private static void SwapDictionary(string newUri)
    {
        var application = Application.Current;
        if (application is null)
            return;

        var merged = application.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(IsLanguageDictionary);
        if (existing != null)
            merged.Remove(existing);
        merged.Add(new ResourceDictionary { Source = new Uri(newUri, UriKind.Absolute) });
    }

    private static ResourceDictionary GetFallbackDictionary()
    {
        var languageCode = CurrentLanguage == "en" ? "en" : "ru";
        if (!CachedDictionaries.TryGetValue(languageCode, out var dictionary))
        {
            dictionary = LoadFallbackDictionary(languageCode);
            CachedDictionaries[languageCode] = dictionary;
        }

        return dictionary;
    }

    private static ResourceDictionary LoadFallbackDictionary(string languageCode)
    {
        var sourcePath = FindFallbackDictionaryPath(languageCode);
        if (sourcePath is not null)
        {
            using var stream = File.OpenRead(sourcePath);
            using var reader = XmlReader.Create(stream);
            return (ResourceDictionary)XamlReader.Load(reader);
        }

        return new ResourceDictionary
        {
            Source = new Uri(
                $"/ScreensView.Viewer;component/Resources/Strings.{languageCode}.xaml",
                UriKind.Relative)
        };
    }

    private static string? FindFallbackDictionaryPath(string languageCode)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "ScreensView.Viewer",
                "Resources",
                $"Strings.{languageCode}.xaml");

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsLanguageDictionary(ResourceDictionary d)
    {
        var src = d.Source?.OriginalString ?? string.Empty;
        return src.Contains("Strings.ru.xaml") || src.Contains("Strings.en.xaml");
    }
}
