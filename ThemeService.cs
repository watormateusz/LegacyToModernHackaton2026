using System;
using System.Linq;
using System.Windows;

namespace WPF
{
    public enum AppTheme { Light, Dark }

    public static class ThemeService
    {
        private const string ThemeDictKey = "ThemeDictionary";

        public static void ApplyTheme(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var rd = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Contains(ThemeDictKey) || (d.Source != null && d.Source.OriginalString.Contains("Themes/Colors.")));

            if (app.Resources.Contains(ThemeDictKey))
            {
                // Nic – podejdziemy inaczej
            }

            var themeDict = app.Resources.MergedDictionaries
                .FirstOrDefault(md => md.Source != null &&
                                      (md.Source.OriginalString.EndsWith("Colors.Light.xaml") ||
                                       md.Source.OriginalString.EndsWith("Colors.Dark.xaml")));
            if (themeDict != null)
                app.Resources.MergedDictionaries.Remove(themeDict);

            // Dodaj nowy
            var newDict = new ResourceDictionary
            {
                Source = new Uri(theme == AppTheme.Light
                    ? "Themes/Colors.Light.xaml"
                    : "Themes/Colors.Dark.xaml", UriKind.Relative)
            };
            app.Resources.MergedDictionaries.Insert(0, newDict); // na początek listy

            ThemeChanged?.Invoke(null, theme);
        }

        public static event Action<object, AppTheme> ThemeChanged;
    }
}