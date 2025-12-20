using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

// --- ПСЕВДОНИМЫ ---
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
// ------------------

namespace FPBooster.UI
{
    public static class ThemeManager
    {
        public static event Action<string>? ThemeChanged;
        public static event Action<Uri>? BackgroundImageChanged;

        private static DispatcherTimer _slideShowTimer;
        public static string CurrentTheme { get; private set; } = "Midnight Blue";
        private static int _currentImageIndex = 0;
        
        private static readonly Dictionary<string, Uri[]> _themeImages = new();

        static ThemeManager()
        {
            // Инициализация путей к картинкам
            _themeImages["Celestial"] = new[] {
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_1.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_2.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_3.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_4.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/celestial_5.png")
            };

            _themeImages["Midnight Blue"] = new[] {
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_1.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_2.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_3.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/midnight_4.png")
            };

            _themeImages["Dark Goldboy"] = new[] {
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/goldboy_1.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/goldboy_2.png"),
                new Uri("pack://application:,,,/FPBooster;component/UI/Resources/goldboy_3.png")
            };

            // Таймер для смены фона каждые 20 секунд
            _slideShowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _slideShowTimer.Tick += (s, e) => NextImage();
            _slideShowTimer.Start();
        }

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

                CurrentTheme = themeName; // Исправлено сохранение имени с пробелами для UI
                
                // Сброс и установка первой картинки новой темы
                _currentImageIndex = -1;
                NextImage();

                ThemeChanged?.Invoke(CurrentTheme);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка применения темы '{key}':\n{ex.Message}");
            }
        }

        private static void NextImage()
        {
            if (!_themeImages.ContainsKey(CurrentTheme)) return;
            var images = _themeImages[CurrentTheme];
            if (images.Length == 0) return;

            _currentImageIndex = (_currentImageIndex + 1) % images.Length;
            BackgroundImageChanged?.Invoke(images[_currentImageIndex]);
        }
    }
}