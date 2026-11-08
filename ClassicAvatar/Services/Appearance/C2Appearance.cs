#nullable enable
// C2 colorway and application-level string management.
// Palettes are ordinary ResourceDictionaries; switching one never rebuilds the
// window, it only swaps the merged dictionary that exposes VBrush.* keys.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using VControls.Theming;

namespace ClassicAvatar.Services
{
    /// <summary>The Light/Dark bases plus 20 color-hunt-inspired colorways.</summary>
    public enum C2Palette
    {
        Light,
        Dark,
        NeoDigitalGlow,
        SereneEarth,
        PeachFuzz,
        ArcticSunrise,
        RetroRevival,
        VelvetNight,
        CitrusPop,
        SoftTech,
        DesertMirage,
        SakuraBloom,
        IndustrialRust,
        AquaHorizon,
        LushForest,
        FuturisticChrome,
        BlushSage,
        Heatwave,
        NordicCalm,
        CosmicPastels,
        RusticAutumn,
        HyperPop,
    }

    /// <summary>Picker metadata: display name, four swatch colours, base-theme flag.</summary>
    public sealed record C2PaletteInfo(
        C2Palette Palette,
        string DisplayName,
        string[] Swatches,
        bool IsBase = false);

    /// <summary>
    /// Applies C2 palettes and the workbench string dictionaries, and remembers
    /// the last selection in <c>%LOCALAPPDATA%\ClassicAvatar\ui-settings.txt</c>.
    /// </summary>
    public static class C2Appearance
    {
        private const string PaletteMarker = "VControls.ThemeName";
        private const string StringsMarker = "C2.Strings.Name";

        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClassicAvatar");

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "ui-settings.txt");

        /// <summary>All 22 colorways, in picker order.</summary>
        public static IReadOnlyList<C2PaletteInfo> Catalog { get; } = new[]
        {
            new C2PaletteInfo(C2Palette.Light, "Light", new[] { "#FFFFFF", "#EEF2F6", "#2563D9", "#142231" }, true),
            new C2PaletteInfo(C2Palette.Dark, "Dark", new[] { "#17151D", "#2B2632", "#D6B36A", "#F3F0F7" }, true),
            new C2PaletteInfo(C2Palette.NeoDigitalGlow, "Neo Digital", new[] { "#00F5D4", "#FF6B6B", "#FEE440", "#2D00F7" }),
            new C2PaletteInfo(C2Palette.SereneEarth, "Serene Earth", new[] { "#C9ADA7", "#9A8C98", "#F2E9E4", "#22223B" }),
            new C2PaletteInfo(C2Palette.PeachFuzz, "Peach Fuzz", new[] { "#FFBE98", "#FFEEDB", "#BA5A31", "#3B252C" }),
            new C2PaletteInfo(C2Palette.ArcticSunrise, "Arctic Sunrise", new[] { "#E0FBFC", "#98C1D9", "#3D5A80", "#EE6C4D" }),
            new C2PaletteInfo(C2Palette.RetroRevival, "Retro Revival", new[] { "#FF5E5B", "#D8A47F", "#FFC145", "#40476D" }),
            new C2PaletteInfo(C2Palette.VelvetNight, "Velvet Night", new[] { "#1B1B3A", "#69306D", "#A5668B", "#D3BCC0" }),
            new C2PaletteInfo(C2Palette.CitrusPop, "Citrus Pop", new[] { "#FFB400", "#FF6363", "#FFE156", "#2D00F7" }),
            new C2PaletteInfo(C2Palette.SoftTech, "Soft Tech", new[] { "#EDF2F4", "#8D99AE", "#EF233C", "#2B2D42" }),
            new C2PaletteInfo(C2Palette.DesertMirage, "Desert Mirage", new[] { "#E07A5F", "#3D405B", "#81B29A", "#F2CC8F" }),
            new C2PaletteInfo(C2Palette.SakuraBloom, "Sakura Bloom", new[] { "#F7D9E3", "#FFD6BA", "#A9DEF9", "#E4C1F9" }),
            new C2PaletteInfo(C2Palette.IndustrialRust, "Industrial Rust", new[] { "#6D597A", "#B56576", "#E56B6F", "#EAAC8B" }),
            new C2PaletteInfo(C2Palette.AquaHorizon, "Aqua Horizon", new[] { "#05668D", "#028090", "#00A896", "#F0F3BD" }),
            new C2PaletteInfo(C2Palette.LushForest, "Lush Forest", new[] { "#354F52", "#52796F", "#84A98C", "#CAD2C5" }),
            new C2PaletteInfo(C2Palette.FuturisticChrome, "Futuristic Chrome", new[] { "#1F1F1F", "#E0E0E0", "#FF4C29", "#009FFD" }),
            new C2PaletteInfo(C2Palette.BlushSage, "Blush & Sage", new[] { "#FAE0E4", "#F7CAD0", "#A0D995", "#6A8E7F" }),
            new C2PaletteInfo(C2Palette.Heatwave, "Heatwave", new[] { "#FF1E56", "#FFAC41", "#2E294E", "#1B998B" }),
            new C2PaletteInfo(C2Palette.NordicCalm, "Nordic Calm", new[] { "#E8E8E4", "#D8E2DC", "#FFCAD4", "#9D8189" }),
            new C2PaletteInfo(C2Palette.CosmicPastels, "Cosmic Pastels", new[] { "#B5EAEA", "#EDF6E5", "#FFBCBC", "#F38BA0" }),
            new C2PaletteInfo(C2Palette.RusticAutumn, "Rustic Autumn", new[] { "#D1495B", "#EDAE49", "#00798C", "#30638E" }),
            new C2PaletteInfo(C2Palette.HyperPop, "Hyper Pop", new[] { "#FF4D6D", "#F9C80E", "#00CFC1", "#2D00F7" }),
        };

        /// <summary>Last opened game Data directory (WZ root).</summary>
        public static string? DataFolder { get; private set; }

        /// <summary>Default .avatar output folder (exe\Outfits by default).</summary>
        public static string SaveFolder { get; private set; } = DefaultSaveFolder;

        /// <summary>Default output folder next to the executable.</summary>
        public static string DefaultSaveFolder =>
            Path.Combine(AppContext.BaseDirectory, "Outfits");

        /// <summary>Persists a new WZ Data directory.</summary>
        public static void SetDataFolder(string? folder)
        {
            DataFolder = string.IsNullOrWhiteSpace(folder) ? null : folder;
            Save();
        }

        /// <summary>Persists a new default .avatar output folder.</summary>
        public static void SetSaveFolder(string folder)
        {
            SaveFolder = string.IsNullOrWhiteSpace(folder) ? DefaultSaveFolder : folder;
            Save();
        }

        /// <summary>Current colorway (defaults to Light).</summary>
        public static C2Palette Palette { get; private set; } = C2Palette.Light;

        /// <summary>Current UI language (defaults to zh-CN).</summary>
        public static VLanguage Language { get; private set; } = VLanguage.ZhCN;

        /// <summary>Raised after a palette dictionary has been applied.</summary>
        public static event EventHandler? PaletteChanged;

        /// <summary>Restores the persisted palette/language and applies them.</summary>
        public static void Initialize()
        {
            Load();

            ApplyPalette(Palette, persist: false);
            ApplyLanguage(Language, persist: false);
        }

        /// <summary>Swaps the C2 palette dictionary at application scope.</summary>
        public static void ApplyPalette(C2Palette palette, bool persist = true)
        {
            Palette = palette;

            if (Application.Current is { } app)
            {
                var uri = new Uri(
                    $"pack://application:,,,/ClassicAvatar;component/Themes/Palettes/{palette}.xaml",
                    UriKind.Absolute);
                VThemeManager.ApplyTheme(app, uri);
            }

            if (persist)
            {
                Save();
            }

            // DynamicResource invalidation is asynchronous. Flushing the render
            // queue here keeps a large 185-card wardrobe from showing a
            // half-themed frame right after the switch.
            if (Application.Current is { } current)
            {
                current.MainWindow?.UpdateLayout();
                current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            }

            PaletteChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Applies a VControls language plus the matching C2 strings.</summary>
        public static void ApplyLanguage(VLanguage language, bool persist = true)
        {
            Language = language;

            if (Application.Current is { } app)
            {
                VLanguageManager.ApplyLanguage(app, language);
                ReplaceStrings(app, language);
            }

            if (persist)
            {
                Save();
            }
        }

        private static void ReplaceStrings(Application app, VLanguage language)
        {
            IList<ResourceDictionary> dictionaries = app.Resources.MergedDictionaries;

            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                if (dictionaries[i].Contains(StringsMarker))
                {
                    dictionaries.RemoveAt(i);
                }
            }

            string name = language switch
            {
                VLanguage.ZhTW => "zh-TW",
                VLanguage.EnUS => "en-US",
                _ => "zh-CN",
            };

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/ClassicAvatar;component/Resources/Strings/C2.{name}.xaml",
                    UriKind.Absolute),
            });
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0)
                    {
                        continue;
                    }

                    string key = line[..split].Trim();
                    string value = line[(split + 1)..].Trim();
                    if (key.Equals("palette", StringComparison.OrdinalIgnoreCase)
                        && Enum.TryParse(value, ignoreCase: true, out C2Palette palette))
                    {
                        Palette = palette;
                    }
                    else if (key.Equals("language", StringComparison.OrdinalIgnoreCase))
                    {
                        Language = value.ToLowerInvariant() switch
                        {
                            "zh-tw" => VLanguage.ZhTW,
                            "en-us" => VLanguage.EnUS,
                            _ => VLanguage.ZhCN,
                        };
                    }
                    else if (key.Equals("dataFolder", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(value))
                    {
                        DataFolder = value;
                    }
                    else if (key.Equals("saveFolder", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(value))
                    {
                        SaveFolder = value;
                    }
                }
            }
            catch (Exception)
            {
                // A corrupt settings file must never block startup.
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                File.WriteAllLines(SettingsFile,
                [
                    $"palette={Palette}",
                    $"language={Language switch { VLanguage.ZhTW => "zh-TW", VLanguage.EnUS => "en-US", _ => "zh-CN" }}",
                    $"dataFolder={DataFolder}",
                    $"saveFolder={SaveFolder}",
                ]);
            }
            catch (Exception)
            {
                // Persistence is a convenience; ignore read-only profiles.
            }
        }
    }
}
