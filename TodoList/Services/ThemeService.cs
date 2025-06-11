using System.Windows;
using System.Windows.Media;

namespace TodoList.Services
{
    public static class ThemeService
    {
        public static event Action ThemeChanged;

        private static bool _isDarkTheme;

        public static void Initialize()
        {
            // Устанавливаем светлую тему по умолчанию
            SetLightTheme();
        }

        public static void ToggleTheme()
        {
            var app = Application.Current;
            var newTheme = _isDarkTheme ? "Light" : "Dark";

            // Удаляем текущую тему
            var currentTheme = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null &&
                                   (d.Source.OriginalString.Contains("LightTheme.xaml") ||
                                    d.Source.OriginalString.Contains("DarkTheme.xaml")));

            if (currentTheme != null)
            {
                app.Resources.MergedDictionaries.Remove(currentTheme);
            }

            // Добавляем новую тему
            var newThemeUri = new Uri($"Themes/{newTheme}Theme.xaml", UriKind.Relative);
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = newThemeUri });

            _isDarkTheme = !_isDarkTheme;
            ThemeChanged?.Invoke();
        }

        private static void SetDarkTheme()
        {
            _isDarkTheme = true;
            var resources = Application.Current.Resources;

            // Обновляем кисти для темной темы
            resources["PrimaryBrush"] = new SolidColorBrush((Color)resources["PrimaryColorDark"]);
            resources["PrimaryDarkBrush"] = new SolidColorBrush((Color)resources["PrimaryDarkColorDark"]);
            resources["PrimaryLightBrush"] = new SolidColorBrush((Color)resources["PrimaryLightColorDark"]);
            resources["WindowBackgroundBrush"] = new SolidColorBrush((Color)resources["WindowBackgroundDark"]);
            resources["CardBackgroundBrush"] = new SolidColorBrush((Color)resources["CardBackgroundDark"]);
            resources["TextBrush"] = new SolidColorBrush((Color)resources["TextColorDark"]);
            resources["SecondaryTextBrush"] = new SolidColorBrush((Color)resources["SecondaryTextColorDark"]);
            resources["BorderBrush"] = new SolidColorBrush((Color)resources["BorderColorDark"]);
            resources["InputBackgroundBrush"] = new SolidColorBrush((Color)resources["CardBackgroundDark"]);

            ThemeChanged?.Invoke();
        }

        private static void SetLightTheme()
        {
            _isDarkTheme = false;
            var resources = Application.Current.Resources;

            // Обновляем кисти для светлой темы
            resources["PrimaryBrush"] = new SolidColorBrush((Color)resources["PrimaryColor"]);
            resources["PrimaryDarkBrush"] = new SolidColorBrush((Color)resources["PrimaryDarkColor"]);
            resources["PrimaryLightBrush"] = new SolidColorBrush((Color)resources["PrimaryLightColor"]);
            resources["WindowBackgroundBrush"] = new SolidColorBrush((Color)resources["WindowBackgroundLight"]);
            resources["CardBackgroundBrush"] = new SolidColorBrush((Color)resources["CardBackgroundLight"]);
            resources["TextBrush"] = new SolidColorBrush((Color)resources["TextColorLight"]);
            resources["SecondaryTextBrush"] = new SolidColorBrush((Color)resources["SecondaryTextColorLight"]);
            resources["BorderBrush"] = new SolidColorBrush((Color)resources["BorderColorLight"]);
            resources["InputBackgroundBrush"] = new SolidColorBrush((Color)resources["WindowBackgroundLight"]);

            ThemeChanged?.Invoke();
        }
    }
}