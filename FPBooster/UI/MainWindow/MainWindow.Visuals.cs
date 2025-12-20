using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Threading;
using FPBooster.UI;

// Псевдонимы
using WinForms = System.Windows.Forms;
using WpfApp = System.Windows.Application;
using MediaBrush = System.Windows.Media.Brush;

namespace FPBooster
{
    public partial class MainWindow
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        // --- ЛОГИКА СЛАЙД-ШОУ (ИНФО) ---
        private DispatcherTimer _infoSlideTimer;
        private int _infoSlideIndex = 0;
        private readonly Uri[] _infoSlideUris = new Uri[]
        {
            new Uri("pack://application:,,,/FPBooster;component/UI/Resources/infoIMG1.png"),
            new Uri("pack://application:,,,/FPBooster;component/UI/Resources/infoIMG2.png"),
            new Uri("pack://application:,,,/FPBooster;component/UI/Resources/infoIMG3.png")
        };

        private void InitInfoSlideshow()
        {
            _infoSlideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _infoSlideTimer.Tick += InfoSlideTick;
            _infoSlideTimer.Start();
            // Сразу ставим первую картинку
            InfoSlideTick(null, null);
        }

        private void InfoSlideTick(object sender, EventArgs e)
        {
            if (InfoSlideshowImage == null) return;

            try
            {
                var uri = _infoSlideUris[_infoSlideIndex];
                var bmp = new BitmapImage(uri);
                if (bmp.CanFreeze) bmp.Freeze();
                
                // АНИМАЦИЯ: Исчезаем в 0, появляемся до 0.15 (чтобы было темно)
                var fadeOut = new DoubleAnimation(0.60, 0, TimeSpan.FromMilliseconds(500));
                
                fadeOut.Completed += (s, ev) =>
                {
                    InfoSlideshowImage.Source = bmp;
                    // Появляемся только до 0.15 (15% яркости)
                    var fadeIn = new DoubleAnimation(0, 0.55, TimeSpan.FromMilliseconds(500));
                    InfoSlideshowImage.BeginAnimation(OpacityProperty, fadeIn);
                };
                
                InfoSlideshowImage.BeginAnimation(OpacityProperty, fadeOut);

                _infoSlideIndex = (_infoSlideIndex + 1) % _infoSlideUris.Length;
            }
            catch { }
        }
        // ------------------------------------

        private void InitTray()
        {
            System.Drawing.Icon trayIconHandle = System.Drawing.SystemIcons.Application;

            try
            {
                Uri iconUri = new Uri("pack://application:,,,/FPBooster;component/UI/Resources/icon.ico");
                var resourceStream = WpfApp.GetResourceStream(iconUri);
                
                if (resourceStream != null)
                {
                    using (var stream = resourceStream.Stream)
                    {
                        trayIconHandle = new System.Drawing.Icon(stream);
                    }
                    
                    // ДОБАВЛЕНО: Устанавливаем иконку для ГЛАВНОГО ОКНА безопасным способом
                    this.Icon = BitmapFrame.Create(iconUri);
                }
            }
            catch 
            {
                // Игнорируем ошибки (окно просто будет без кастомной иконки, но не упадет)
            }

            _trayIcon = new WinForms.NotifyIcon 
            { 
                Icon = trayIconHandle, 
                Visible = false, 
                Text = "FPBooster" 
            };
            
            _trayIcon.Click += (s, e) => RestoreFromEcoMode();
            
            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("Развернуть", null, (s, e) => RestoreFromEcoMode());
            trayMenu.Items.Add("Выход", null, (s, e) => { _trayIcon.Visible = false; WpfApp.Current.Shutdown(); });
            _trayIcon.ContextMenuStrip = trayMenu;
        }

        public void SwitchToPluginView(object viewContent, string pluginName)
        {
            DashboardGrid.IsHitTestVisible = false;
            PluginHost.Content = viewContent;
            PluginArea.Visibility = Visibility.Visible;
            PluginArea.IsHitTestVisible = true;
            PluginArea.Opacity = 0;

            if (FindName("PluginTranslate") is TranslateTransform pt) pt.X = 150; 
            if (FindName("PluginScale") is ScaleTransform ps) { ps.ScaleX = 0.95; ps.ScaleY = 0.95; }
            
            var dashFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var dashScaleAnim = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(200));
            
            if (FindName("DashboardGrid") is Grid dash)
            {
                dash.BeginAnimation(OpacityProperty, dashFade);
                if (FindName("DashScale") is ScaleTransform ds) ds.BeginAnimation(ScaleTransform.ScaleXProperty, dashScaleAnim);
            }

            var pluginFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(100) };
            var pluginSlide = new DoubleAnimation(150, 0, TimeSpan.FromMilliseconds(350)) { BeginTime = TimeSpan.FromMilliseconds(50), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var pluginZoom = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = TimeSpan.FromMilliseconds(50), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

            dashFade.Completed += (_, __) => { if(DashboardGrid != null) DashboardGrid.Visibility = Visibility.Collapsed; };

            PluginArea.BeginAnimation(OpacityProperty, pluginFade);
            if (FindName("PluginTranslate") is TranslateTransform pt2) pt2.BeginAnimation(TranslateTransform.XProperty, pluginSlide);
            if (FindName("PluginScale") is ScaleTransform ps2) { ps2.BeginAnimation(ScaleTransform.ScaleXProperty, pluginZoom); ps2.BeginAnimation(ScaleTransform.ScaleYProperty, pluginZoom); }

            if (ThemeManager.CurrentTheme == "Celestial") { ThemeParticles.Visibility = Visibility.Collapsed; PluginParticles.Visibility = Visibility.Visible; }
        }

        private void OnBackFromPlugin_Click(object sender, RoutedEventArgs e)
        {
            PluginArea.IsHitTestVisible = false;
            DashboardGrid.Visibility = Visibility.Visible;
            DashboardGrid.IsHitTestVisible = true;
            DashboardGrid.Opacity = 0;

            if (FindName("DashScale") is ScaleTransform dsReset) { dsReset.ScaleX = 0.95; dsReset.ScaleY = 0.95; }

            var pluginFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var pluginSlide = new DoubleAnimation(0, 150, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };

            PluginArea.BeginAnimation(OpacityProperty, pluginFade);
            if (FindName("PluginTranslate") is TranslateTransform pt) pt.BeginAnimation(TranslateTransform.XProperty, pluginSlide);

            var dashFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(50) };
            var dashScaleAnim = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(50), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

            if (FindName("DashboardGrid") is Grid dash)
            {
                dash.BeginAnimation(OpacityProperty, dashFade);
                if (FindName("DashScale") is ScaleTransform ds) ds.BeginAnimation(ScaleTransform.ScaleXProperty, dashScaleAnim);
            }

            pluginFade.Completed += (_, __) => { PluginArea.Visibility = Visibility.Collapsed; PluginHost.Content = null; };

            if (ThemeManager.CurrentTheme == "Celestial") { ThemeParticles.Visibility = Visibility.Visible; PluginParticles.Visibility = Visibility.Collapsed; }
        }

        private void OnEcoModeClick(object sender, RoutedEventArgs e)
        {
            var dlg = new UI.ThemedDialog("Режим Eco Mode", 
                "Этот режим оптимизирует работу программы для слабых ПК.\n\n• Окно свернется в трей\n• Отключится графика\n• Снизится потребление RAM/CPU\n\nЗадачи продолжат работать. Включить?", true) { Owner = this };
            
            if (dlg.ShowDialog() == true) EnterEcoMode();
        }

        private void EnterEcoMode()
        {
            _inEcoMode = true;
            this.Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(3000, "FPBooster", "Работает в фоне", WinForms.ToolTipIcon.Info);
            ThemeParticles.Children.Clear(); PluginParticles.Children.Clear();
            try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1); } catch { }
            Log("[ECO] Демо-режим включен. Работаем в фоне.");
        }

        private void RestoreFromEcoMode()
        {
            _inEcoMode = false;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            _trayIcon.Visible = false;
            ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);
            Log("[ECO] Интерфейс восстановлен.");
        }

        private void ShowParticlesForTheme(string t)
        {
            ThemeParticles.Children.Clear(); PluginParticles.Children.Clear();
            if (t.Replace(" ","") == "Celestial") { CreateParticles(ThemeParticles); if (PluginArea.Visibility == Visibility.Visible) CreateParticles(PluginParticles); }
        }
        
        private void CreateParticles(Canvas c)
        {
            for (int i=0; i<20; i++) {
                var el = new System.Windows.Shapes.Ellipse { Width=_rng.Next(1,4), Height=_rng.Next(1,4), Fill=(MediaBrush)FindResource("BrushAccentLight"), Opacity=_rng.NextDouble()*0.7 };
                Canvas.SetLeft(el, _rng.NextDouble()*ActualWidth); Canvas.SetTop(el, _rng.NextDouble()*ActualHeight);
                c.Children.Add(el);
                var anim = new DoubleAnimation { To=0, Duration=TimeSpan.FromSeconds(_rng.Next(2,6)), AutoReverse=true, RepeatBehavior=RepeatBehavior.Forever };
                el.BeginAnimation(OpacityProperty, anim);
            }
        }

        public string GetCurrentTheme() => ThemeManager.CurrentTheme;
        private void ShowThemed(string t, string m) => new UI.ThemedDialog(t, m) { Owner = this }.ShowDialog();
        
        private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item) { ThemeManager.ApplyTheme(item.Content.ToString() ?? "Midnight Blue"); TryPersistImmediate(); }
        }
        
        private void OnThemeChanged(string newTheme) { ThemeName.Text = newTheme; ShowParticlesForTheme(newTheme); }
        
        private void OnBackgroundImageChanged(Uri imageUri)
        {
            var target = PluginArea.Visibility == Visibility.Visible ? PluginBannerImage : BackgroundImage;
            var fadeOut = new DoubleAnimation(0.3, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, __) =>
            {
                try {
                    var bmp = new BitmapImage(imageUri);
                    if (bmp.CanFreeze) bmp.Freeze();
                    target.Source = bmp;
                    target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.3, TimeSpan.FromMilliseconds(500)));
                } catch { }
            };
            target.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}