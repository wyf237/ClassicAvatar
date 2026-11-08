using System.Windows;

namespace VControls.Theming;

/// <summary>
/// Applies and swaps VControls language dictionaries at application scope.
/// Each language dictionary exposes string resources keyed with the
/// <c>VLoc.</c> prefix. Applications reference these via <c>DynamicResource</c>
/// so a language change updates every label instantly.
/// </summary>
public static class VLanguageManager
{
    /// <summary>
    /// Key placed in every language dictionary. It lets the manager remove the
    /// previously applied language without disturbing other dictionaries.
    /// </summary>
    public const string LanguageMarkerKey = "VControls.LanguageName";

    private static VLanguage _currentLanguage = VLanguage.ZhCN;

    /// <summary>Raised after a language dictionary has been applied.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>The last language applied by the manager.</summary>
    public static VLanguage CurrentLanguage => _currentLanguage;

    /// <summary>Applies a language to <see cref="Application.Current"/>.</summary>
    public static void ApplyLanguage(VLanguage language)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("VLanguageManager requires a WPF Application.");

        ApplyLanguage(application, language);
    }

    /// <summary>
    /// Applies a custom language dictionary. This is the extension point for
    /// languages not yet present in <see cref="VLanguage"/> (for example Japanese).
    /// The dictionary must expose the same <c>VLoc.*</c> keys as the built-in files.
    /// </summary>
    public static void ApplyLanguage(Application application, Uri languageDictionaryUri)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(languageDictionaryUri);

        ReplaceLanguageDictionary(
            application,
            new ResourceDictionary { Source = languageDictionaryUri });
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Applies a language to the specified application.</summary>
    public static void ApplyLanguage(Application application, VLanguage language)
    {
        ArgumentNullException.ThrowIfNull(application);

        var dictionary = new ResourceDictionary
        {
            Source = CreateLanguageUri(language)
        };

        ReplaceLanguageDictionary(application, dictionary);
        _currentLanguage = language;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Returns the pack URI for a language dictionary.</summary>
    public static Uri CreateLanguageUri(VLanguage language)
    {
        var assemblyName = typeof(VLanguageManager).Assembly.GetName().Name;
        var name = language switch
        {
            VLanguage.ZhTW => "zh-TW",
            VLanguage.EnUS => "en-US",
            _ => "zh-CN"
        };
        return new Uri(
            $"pack://application:,,,/{assemblyName};component/Themes/Languages/{name}.xaml",
            UriKind.Absolute);
    }

    private static void ReplaceLanguageDictionary(Application application, ResourceDictionary dictionary)
    {
        var dictionaries = application.Resources.MergedDictionaries;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (dictionaries[i].Contains(LanguageMarkerKey))
            {
                dictionaries.RemoveAt(i);
            }
        }

        // Keep language early in the merge order so application-specific
        // overrides added later still win.
        dictionaries.Insert(0, dictionary);
    }
}
