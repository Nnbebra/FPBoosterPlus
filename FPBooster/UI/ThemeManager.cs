using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

// --- ИСПРАВЛЕНИЕ КОНФЛИКТОВ ---
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
// ------------------------------

namespace FPBooster.UI
{
    public static class ThemeManager
    {
        public static event Action<string>? ThemeChanged;
        public static event Action<Uri>? BackgroundImageChanged;

        private static DispatcherTimer _slideShowTimer;
        private static string _currentTheme = "MidnightBlue";
        private static int _currentImageIndex = 0;
        
        private static readonly Dictionary<string, Uri[]> _themeImages = new();

        static ThemeManager()
        {
            _themeImages["Celestial"] = new[] {
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_1.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_2.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_3.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_4.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_5.png")
            };

            _themeImages["MidnightBlue"] = new[] {
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_1.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_2.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_3.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_4.png")
            };
            
            _themeImages["DarkGoldboy"] = new[] {
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/goldboy_1.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/goldboy_2.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/goldboy_3.png")
            };

            _slideShowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _slideShowTimer.Tick += (s, e) => NextImage();
            _slideShowTimer.Start();
        }

        public static string CurrentTheme => _currentTheme;

        public static void ApplyTheme(string themeName)
        {
            var key = themeName.Replace(" ", "");
            try
            {
                var uriString = $"pack://application:,,,/FPBooster;component/UI/Themes/{key}.xaml";
                var dict = new ResourceDictionary
                {
                    Source = new Uri(uriString, UriKind.Absolute)
                };

                var oldDicts = Application.Current.Resources.MergedDictionaries
                    .Where(d => d.Source != null && d.Source.OriginalString.Contains("/UI/Themes/"))
                    .ToList();

                foreach (var d in oldDicts)
                    Application.Current.Resources.MergedDictionaries.Remove(d);

                Application.Current.Resources.MergedDictionaries.Add(dict);

                _currentTheme = key;
                _currentImageIndex = -1;
                NextImage();

                ThemeChanged?.Invoke(_currentTheme);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка применения темы '{key}':\n{ex.Message}");
            }
        }

        private static void NextImage()
        {
            if (!_themeImages.ContainsKey(_currentTheme)) return;
            var images = _themeImages[_currentTheme];
            if (images.Length == 0) return;

            _currentImageIndex = (_currentImageIndex + 1) % images.Length;
            BackgroundImageChanged?.Invoke(images[_currentImageIndex]);
        }
    }
}