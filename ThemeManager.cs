using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace BluetoothWidget
{
    public enum AppTheme
    {
        Retro,      // Original cyan/magenta synthwave
        Pixel,      // Terminal green CRT style
        NeonDrift,  // Warm purple/pink/cyan synthwave
        Moss        // Forest nature theme
    }

    public class ThemeSettings
    {
        public string Theme { get; set; } = "Retro";
    }

    public static class ThemeManager
    {
        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothWidget", "theme.json");

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Retro;

        public static event Action? ThemeChanged;

        static ThemeManager()
        {
            Load();
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                    if (settings != null && Enum.TryParse<AppTheme>(settings.Theme, out var theme))
                    {
                        CurrentTheme = theme;
                    }
                }
            }
            catch { }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                var json = JsonSerializer.Serialize(new ThemeSettings { Theme = CurrentTheme.ToString() });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        public static void SetTheme(AppTheme theme)
        {
            if (CurrentTheme == theme) return;
            CurrentTheme = theme;
            Save();
            ApplyTheme();
            ThemeChanged?.Invoke();
        }

        public static void ToggleTheme()
        {
            // Cycle through all 4 themes: Retro -> Pixel -> NeonDrift -> Moss -> Retro
            var nextTheme = CurrentTheme switch
            {
                AppTheme.Retro => AppTheme.Pixel,
                AppTheme.Pixel => AppTheme.NeonDrift,
                AppTheme.NeonDrift => AppTheme.Moss,
                AppTheme.Moss => AppTheme.Retro,
                _ => AppTheme.Retro
            };
            SetTheme(nextTheme);
        }

        public static void ApplyTheme()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Resources.MergedDictionaries.Clear();

            var themePath = CurrentTheme switch
            {
                AppTheme.Retro => "Themes/RetroTheme.xaml",
                AppTheme.Pixel => "Themes/PixelTheme.xaml",
                AppTheme.NeonDrift => "Themes/NeonDriftTheme.xaml",
                AppTheme.Moss => "Themes/MossTheme.xaml",
                _ => "Themes/RetroTheme.xaml"
            };

            var themeDict = new ResourceDictionary
            {
                Source = new Uri(themePath, UriKind.Relative)
            };
            app.Resources.MergedDictionaries.Add(themeDict);
        }
    }
}
