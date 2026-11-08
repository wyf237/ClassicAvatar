using System.Windows;

namespace VControls.Theming;

/// <summary>
/// Applies and swaps VControls theme dictionaries at application scope.
/// The library ships with Light and Dark themes; applications can add their
/// own dictionary with the same semantic VBrush.* keys and merge it the same way.
/// </summary>
public static class VThemeManager
{
    /// <summary>
    /// Key placed in every built-in theme dictionary. It lets the manager
    /// remove the previously applied theme without disturbing other dictionaries.
    /// </summary>
    public const string ThemeMarkerKey = "VControls.ThemeName";

    private static VTheme _currentTheme = VTheme.Light;

    /// <summary>Raised after a theme dictionary has been applied.</summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>The last built-in theme applied by the manager.</summary>
    public static VTheme CurrentTheme => _currentTheme;

    /// <summary>Applies a built-in theme to <see cref="Application.Current"/>.</summary>
    public static void ApplyTheme(VTheme theme)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("VThemeManager requires a WPF Application.");

        ApplyTheme(application, theme);
    }

    /// <summary>Applies a built-in theme to the specified application.</summary>
    public static void ApplyTheme(Application application, VTheme theme)
    {
        ArgumentNullException.ThrowIfNull(application);

        var dictionary = new ResourceDictionary
        {
            Source = CreateThemeUri(theme)
        };

        ReplaceThemeDictionary(application, dictionary);
        _currentTheme = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a custom theme dictionary. The dictionary should expose the same
    /// semantic VBrush.* keys as the built-in palettes.
    /// </summary>
    public static void ApplyTheme(Application application, Uri themeDictionaryUri)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(themeDictionaryUri);

        ReplaceThemeDictionary(
            application,
            new ResourceDictionary { Source = themeDictionaryUri });
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Returns the pack URI for a built-in theme dictionary.</summary>
    public static Uri CreateThemeUri(VTheme theme)
    {
        var assemblyName = typeof(VThemeManager).Assembly.GetName().Name;
        var themeName = theme == VTheme.Dark ? "Dark" : "Light";
        return new Uri(
            $"pack://application:,,,/{assemblyName};component/Themes/{themeName}.xaml",
            UriKind.Absolute);
    }

    private static void ReplaceThemeDictionary(Application application, ResourceDictionary dictionary)
    {
        var dictionaries = application.Resources.MergedDictionaries;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (dictionaries[i].Contains(ThemeMarkerKey))
            {
                dictionaries.RemoveAt(i);
            }
        }

        // Keep the theme early in the merge order so application-specific
        // overrides added later still win.
        dictionaries.Insert(0, dictionary);
    }
}
