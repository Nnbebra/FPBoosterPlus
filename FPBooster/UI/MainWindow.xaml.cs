using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; 
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Net.Http;

using FPBooster.FunPay;
using FPBooster.UI;
using FPBooster.ServerApi;
using FPBooster.Plugins;

// --- ПСЕВДОНИМЫ ---
using WinForms = System.Windows.Forms; 
using Application = System.Windows.Application; 
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataFormats = System.Windows.DataFormats;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
// ------------------

namespace FPBooster
{
    public partial class MainWindow : Window
    {
        public class LogEntry
        {
            public required string Text { get; set; }
            public required Brush Color { get; set; }
        }

        private FPBooster.Config.ConfigManager.ConfigData _config = new();
        private readonly ObservableCollection<LogEntry> _logEntries = new();

        private string _cachedGoldenKey = "";
        private string _cachedUserName = "";
        private bool _isKeyValid = false; 

        private bool _isLoaded = false;
        private readonly Random _rng = new();
        private DispatcherTimer _saveTimer;
        private WinForms.NotifyIcon _trayIcon;
        private bool _inEcoMode = false;

        [DllImport("kernel32.dll")] 
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        public MainWindow()
        {
            InitializeComponent();
            
            // ОПТИМИЗАЦИЯ ГРАФИКИ
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality); // Ускоряет отрисовку картинок
            
            LogBox.ItemsSource = _logEntries;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveTimer.Tick += async (s, e) => { _saveTimer.Stop(); await SaveStoreAsync(); };
            
            SizeChanged += (_, __) => { 
                if (ThemeParticles.Visibility == Visibility.Visible) 
                    ShowParticlesForTheme(ThemeManager.CurrentTheme);
            };

            InitTray();

            if (ThemeCombo.Items.Count > 0) ThemeCombo.SelectedIndex = 0;
            
            var copyCmd = new RoutedCommand(); 
            copyCmd.InputGestures.Add(new KeyGesture(Key.C, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(copyCmd, (_, __) => CopySelectedNodesToClipboard()));

            var selectAllCmd = new RoutedCommand(); 
            selectAllCmd.InputGestures.Add(new KeyGesture(Key.A, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(selectAllCmd, (_, __) => NodeList.SelectAll()));

            ThemeManager.BackgroundImageChanged += OnBackgroundImageChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;

            SetNodesInputEnabled(false);
        }

        private void InitTray()
        {
            System.Drawing.Icon trayIconHandle = System.Drawing.SystemIcons.Application;
            try
            {
                // Попытка загрузки иконки из ресурсов
                Uri iconUri = new Uri("pack://application:,,,/FPBooster;component/UI/Resources/icon.ico");
                var resourceStream = Application.GetResourceStream(iconUri);
                if (resourceStream != null)
                {
                    using (var stream = resourceStream.Stream)
                    {
                        trayIconHandle = new System.Drawing.Icon(stream);
                    }
                }
            }
            catch 
            {
                // Если ресурсы не найдены, пробуем файл
                if (System.IO.File.Exists("icon.ico")) try { trayIconHandle = new System.Drawing.Icon("icon.ico"); } catch { }
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
            trayMenu.Items.Add("Выход", null, (s, e) => { _trayIcon.Visible = false; Application.Current.Shutdown(); });
            _trayIcon.ContextMenuStrip = trayMenu;
        }

        // --- ОПТИМИЗИРОВАННАЯ АНИМАЦИЯ ПЕРЕХОДОВ ---
        
        public void SwitchToPluginView(object viewContent, string pluginName)
        {
            // 1. Блокируем главное меню
            DashboardGrid.IsHitTestVisible = false;

            PluginHost.Content = viewContent;
            PluginArea.Visibility = Visibility.Visible;
            PluginArea.IsHitTestVisible = true; // Разрешаем клики в плагине
            PluginArea.Opacity = 0;

            // Сброс позиций
            if (FindName("PluginTranslate") is TranslateTransform pt) pt.X = 150; 
            if (FindName("PluginScale") is ScaleTransform ps) { ps.ScaleX = 0.95; ps.ScaleY = 0.95; }
            
            // Анимация скрытия меню
            var dashFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var dashScaleAnim = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(200));
            
            if (FindName("DashboardGrid") is Grid dash)
            {
                dash.BeginAnimation(OpacityProperty, dashFade);
                if (FindName("DashScale") is ScaleTransform ds)
                {
                    ds.BeginAnimation(ScaleTransform.ScaleXProperty, dashScaleAnim);
                    ds.BeginAnimation(ScaleTransform.ScaleYProperty, dashScaleAnim);
                }
            }

            // Анимация появления плагина
            var pluginFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(100) };
            var pluginSlide = new DoubleAnimation(150, 0, TimeSpan.FromMilliseconds(350)) 
            { 
                BeginTime = TimeSpan.FromMilliseconds(50), 
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } 
            };
            var pluginZoom = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(350)) 
            { 
                BeginTime = TimeSpan.FromMilliseconds(50), 
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } 
            };

            // Скрываем меню полностью после завершения анимации (экономит ресурсы)
            dashFade.Completed += (_, __) => { if(DashboardGrid != null) DashboardGrid.Visibility = Visibility.Collapsed; };

            PluginArea.BeginAnimation(OpacityProperty, pluginFade);
            if (FindName("PluginTranslate") is TranslateTransform pt2) pt2.BeginAnimation(TranslateTransform.XProperty, pluginSlide);
            if (FindName("PluginScale") is ScaleTransform ps2) 
            {
                ps2.BeginAnimation(ScaleTransform.ScaleXProperty, pluginZoom);
                ps2.BeginAnimation(ScaleTransform.ScaleYProperty, pluginZoom);
            }

            if (ThemeManager.CurrentTheme == "Celestial") {
                ThemeParticles.Visibility = Visibility.Collapsed;
                PluginParticles.Visibility = Visibility.Visible;
            }
        }

        private void OnBackFromPlugin_Click(object sender, RoutedEventArgs e)
        {
            // 1. Блокируем плагин, включаем меню
            PluginArea.IsHitTestVisible = false;
            DashboardGrid.Visibility = Visibility.Visible;
            DashboardGrid.IsHitTestVisible = true;
            DashboardGrid.Opacity = 0; // Начинаем с прозрачного, чтобы не было "мигания"

            if (FindName("DashScale") is ScaleTransform dsReset) { dsReset.ScaleX = 0.95; dsReset.ScaleY = 0.95; }

            // Анимация ухода плагина
            var pluginFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var pluginSlide = new DoubleAnimation(0, 150, TimeSpan.FromMilliseconds(200)) 
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };

            PluginArea.BeginAnimation(OpacityProperty, pluginFade);
            if (FindName("PluginTranslate") is TranslateTransform pt) pt.BeginAnimation(TranslateTransform.XProperty, pluginSlide);

            // Анимация возврата меню
            var dashFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(50) };
            var dashScaleAnim = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(300)) 
            { 
                BeginTime = TimeSpan.FromMilliseconds(50), 
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } 
            };

            if (FindName("DashboardGrid") is Grid dash)
            {
                dash.BeginAnimation(OpacityProperty, dashFade);
                if (FindName("DashScale") is ScaleTransform ds)
                {
                    ds.BeginAnimation(ScaleTransform.ScaleXProperty, dashScaleAnim);
                    ds.BeginAnimation(ScaleTransform.ScaleYProperty, dashScaleAnim);
                }
            }

            pluginFade.Completed += (_, __) =>
            {
                PluginArea.Visibility = Visibility.Collapsed;
                PluginHost.Content = null; 
            };

            if (ThemeManager.CurrentTheme == "Celestial") {
                ThemeParticles.Visibility = Visibility.Visible;
                PluginParticles.Visibility = Visibility.Collapsed;
            }
        }
        // ------------------------------------

        // --- СТАНДАРТНЫЕ МЕТОДЫ ---
        public void SaveStore() => SaveStoreSync();
        public void Log(string msg) => AppendLog(msg);
        public string GetGoldenKey() => GoldenKeyInput.Text?.Trim() ?? "";
        
        public List<string> GetActiveNodeIds()
        {
            return NodeList.Items.Cast<object>()
                   .Select(i => i?.ToString() ?? "")
                   .Where(s => !string.IsNullOrWhiteSpace(s))
                   .ToList();
        }

        public ObservableCollection<LogEntry> GetLogCollection() => _logEntries;
        public string GetCurrentTheme() => ThemeManager.CurrentTheme;

        private void SetNodesInputEnabled(bool enabled)
        {
            _isKeyValid = enabled;
            NodeInput.IsEnabled = enabled;
            NodeInput.Opacity = enabled ? 1.0 : 0.5;
            
            if (!enabled && string.IsNullOrEmpty(_cachedUserName)) LicenseStatus.Text = "Ожидание ключа...";
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try {
                if (CloudApiClient.Instance.TryLoadToken()) AppendLog("[INFO] Cloud подключен", Brushes.LightGreen);
                
                _isLoaded = false;
                _config = FPBooster.Config.ConfigManager.Load();
                
                ThemeManager.ApplyTheme(!string.IsNullOrEmpty(_config.Theme) ? _config.Theme : "Midnight Blue");
                foreach (ComboBoxItem item in ThemeCombo.Items) 
                    if (item.Content.ToString() == ThemeManager.CurrentTheme) ThemeCombo.SelectedItem = item;

                _cachedGoldenKey = _config.GoldenKey ?? "";
                GoldenKeyInput.Text = _cachedGoldenKey;
                GoldenKeyMasked.Text = string.IsNullOrWhiteSpace(_cachedGoldenKey) ? "—" : Mask(_cachedGoldenKey);
                
                var savedNodes = (_config.NodeIds ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
                NodeList.Items.Clear();
                foreach (var nid in savedNodes) NodeList.Items.Add(nid);
                NodeCount.Text = NodeList.Items.Count.ToString();

                if (!string.IsNullOrEmpty(_cachedGoldenKey))
                {
                    SetNodesInputEnabled(true);
                    if (!string.IsNullOrEmpty(_config.UserName)) 
                    {
                        _cachedUserName = _config.UserName;
                        LicenseStatus.Text = $"Аккаунт: {_cachedUserName}";
                    }
                }

                _isLoaded = true;
                Dispatcher.BeginInvoke(() => ShowParticlesForTheme(ThemeManager.CurrentTheme), DispatcherPriority.ApplicationIdle);
                AppendLog("[INFO] Готов к работе");
            } 
            catch (Exception ex) { _isLoaded = true; AppendLog($"[ERR] {ex.Message}", Brushes.IndianRed); }
        }

        private async void SaveGoldenKey_Click(object sender, RoutedEventArgs e)
        {
            var k = GoldenKeyInput.Text?.Trim();
            if (string.IsNullOrEmpty(k)) { ShowThemed("Ошибка", "Введите Golden Key!"); SetNodesInputEnabled(false); return; }

            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳"; }

            try
            {
                var client = ProfileParser.CreateClient(k);
                var userId = await ProfileParser.GetUserIdAsync(client);

                if (userId != null)
                {
                    var userName = await ProfileParser.GetUserNameAsync(client, userId);
                    
                    _cachedGoldenKey = k;
                    _cachedUserName = userName; 
                    
                    LicenseStatus.Text = $"Аккаунт: {userName}";
                    GoldenKeyMasked.Text = Mask(k);
                    
                    ShowThemed("Успех", $"Ключ принят!\nДобро пожаловать, {userName}!");
                    AppendLog($"[AUTH] Вход выполнен: {userName}", Brushes.Lime);
                    
                    SetNodesInputEnabled(true);
                    TryPersistImmediate();
                }
                else
                {
                    SetNodesInputEnabled(false);
                    ShowThemed("Ошибка", "Невалидный Golden Key!\nПроверьте, правильно ли вы его скопировали.");
                    AppendLog("[ERR] Неверный ключ", Brushes.IndianRed);
                }
            }
            catch (Exception ex)
            {
                SetNodesInputEnabled(false);
                ShowThemed("Ошибка сети", ex.Message);
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = "💾"; }
            }
        }

        private async void OnExtractNodesClick(object sender, RoutedEventArgs e)
        {
            if (!_isKeyValid) { ShowThemed("Доступ запрещен", "Сначала подтвердите Golden Key!"); return; }

            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳"; }

            AppendLog("[INFO] Сканирование профиля...", Brushes.Cyan);
            try
            {
                var client = ProfileParser.CreateClient(_cachedGoldenKey);
                var userId = await ProfileParser.GetUserIdAsync(client);
                
                if (string.IsNullOrEmpty(userId)) { AppendLog("[ERR] Ошибка доступа к профилю", Brushes.IndianRed); return; }

                var nodes = await ProfileParser.ScanProfileForLots(client, userId);
                
                if (nodes.Count == 0) { AppendLog("[WARN] Активных разделов не найдено", Brushes.Orange); return; }

                int added = 0;
                foreach (var nid in nodes) {
                    bool exists = false;
                    foreach (var item in NodeList.Items) if (item.ToString() == nid) { exists = true; break; }
                    
                    if (!exists) {
                        NodeList.Items.Add(nid);
                        added++;
                    }
                }

                NodeCount.Text = NodeList.Items.Count.ToString();
                AppendLog($"[SUCCESS] Найдено и добавлено разделов: {added}", Brushes.Lime);
                if (added > 0) TryPersistImmediate();
            }
            catch (Exception ex) { AppendLog($"[ERR] {ex.Message}", Brushes.IndianRed); }
            finally { if (btn != null) { btn.IsEnabled = true; btn.Content = "🔄"; } }
        }

        private void AddNode_Click(object sender, RoutedEventArgs e) => AddNodeInternal(NodeInput.Text);

        private void AddNodeInternal(string? text)
        {
            if (!_isKeyValid) { ShowThemed("Ошибка", "Сначала введите Golden Key!"); return; }

            var raw = text?.Trim() ?? "";
            var nid = "";
            
            if (Regex.IsMatch(raw, @"^\d+$")) nid = raw;
            else 
            {
                var m = Regex.Match(raw, @"/lots/(\d+)/");
                if (m.Success) nid = m.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(nid))
            {
                ShowThemed("Некорректные данные", "Введите только цифры ID раздела\nили полную ссылку на раздел.");
                return;
            }

            foreach (var item in NodeList.Items)
            {
                if (item.ToString() == nid)
                {
                    AppendLog($"[INFO] Раздел {nid} уже есть в списке", Brushes.Gray);
                    NodeInput.Clear();
                    return;
                }
            }

            NodeList.Items.Add(nid);
            NodeCount.Text = NodeList.Items.Count.ToString();
            AppendLog($"[ADD] Раздел добавлен: {nid}", Brushes.White);
            NodeInput.Clear();
            TryPersistImmediate();
        }

        private void OnEcoModeClick(object sender, RoutedEventArgs e)
        {
            string title = "Режим Eco Mode";
            string description = 
                "Этот режим оптимизирует работу программы для слабых ПК.\n\n" +
                "• Окно свернется в трей\n" +
                "• Отключится графика\n" +
                "• Снизится потребление RAM/CPU\n\n" +
                "Задачи продолжат работать. Включить?";

            var dlg = new UI.ThemedDialog(title, description, true) { Owner = this };
            
            if (dlg.ShowDialog() == true)
            {
                EnterEcoMode();
            }
        }

        private void EnterEcoMode()
        {
            _inEcoMode = true;
            this.Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(3000, "FPBooster", "Работает в фоне", WinForms.ToolTipIcon.Info);
            
            ThemeParticles.Children.Clear();
            PluginParticles.Children.Clear();
            
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

        private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item) { ThemeManager.ApplyTheme(item.Content.ToString() ?? "Midnight Blue"); TryPersistImmediate(); }
        }
        private void OnThemeChanged(string newTheme) { ThemeName.Text = newTheme; ShowParticlesForTheme(newTheme); }
        
        private void OnBackgroundImageChanged(Uri imageUri)
        {
            var target = PluginArea.Visibility == Visibility.Visible ? PluginBannerImage : RightBackgroundImage;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
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

        public void AppendLog(string msg, Brush? color = null)
        {
            if (_logEntries.Count > 200) _logEntries.RemoveAt(0);
            var c = color ?? Brushes.White;
            if (c.CanFreeze) c.Freeze();
            _logEntries.Add(new LogEntry { Text = msg, Color = c });
            Dispatcher.InvokeAsync(() => { try { if (VisualTreeHelper.GetChild(LogBox, 0) is Decorator border && border.Child is ScrollViewer sv) sv.ScrollToBottom(); } catch {} });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) { _logEntries.Clear(); AppendLog("[CLR] Очищено"); }
        private void RemoveNode_Click(object sender, RoutedEventArgs e) {
            var selected = new List<object>(NodeList.SelectedItems.Cast<object>());
            foreach (var s in selected) NodeList.Items.Remove(s);
            NodeCount.Text = NodeList.Items.Count.ToString(); TryPersistImmediate();
        }
        private void ClearNodes_Click(object sender, RoutedEventArgs e) { NodeList.Items.Clear(); NodeCount.Text="0"; TryPersistImmediate(); }

        private async Task SaveStoreAsync() { if (!_isLoaded) return; var cfg = GetConfig(); await Task.Run(() => { try { FPBooster.Config.ConfigManager.Save(cfg); } catch { } }); }
        private void SaveStoreSync() { if (!_isLoaded) return; try { FPBooster.Config.ConfigManager.Save(GetConfig()); } catch { } }
        private FPBooster.Config.ConfigManager.ConfigData GetConfig() => new() { GoldenKey = GoldenKeyInput.Text?.Trim() ?? "", NodeIds = NodeList.Items.Cast<object>().Select(i => i.ToString() ?? "").ToList(), Theme = ThemeManager.CurrentTheme, UserName = _cachedUserName };
        private void TryPersistImmediate() { _saveTimer.Stop(); _saveTimer.Start(); }

        private void ShowParticlesForTheme(string t) {
            ThemeParticles.Children.Clear(); PluginParticles.Children.Clear();
            if (t.Replace(" ","") == "Celestial") { CreateParticles(ThemeParticles); if (PluginArea.Visibility == Visibility.Visible) CreateParticles(PluginParticles); }
        }
        
        // ОПТИМИЗАЦИЯ: Уменьшено количество частиц для плавности
        private void CreateParticles(Canvas c) {
            for (int i=0; i<20; i++) {
                var el = new System.Windows.Shapes.Ellipse { Width=_rng.Next(1,4), Height=_rng.Next(1,4), Fill=(Brush)FindResource("BrushAccentLight"), Opacity=_rng.NextDouble()*0.7 };
                Canvas.SetLeft(el, _rng.NextDouble()*ActualWidth); Canvas.SetTop(el, _rng.NextDouble()*ActualHeight);
                c.Children.Add(el);
                var anim = new DoubleAnimation { To=0, Duration=TimeSpan.FromSeconds(_rng.Next(2,6)), AutoReverse=true, RepeatBehavior=RepeatBehavior.Forever };
                el.BeginAnimation(OpacityProperty, anim);
            }
        }

        private void OpenAutoBump_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "auto_bump");
        private void OpenLotsToggle_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "lots_toggle");
        private void OpenLotsDelete_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "lots_delete");
        private void OpenAutoRestock_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "auto_restock");
        private void OpenAdvProfileStat_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "adv_profile_stat");
        private void OpenPlusCloud_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "fp_plus_dashboard");
        private void OnMoreButtonClick(object s, RoutedEventArgs e) { new UI.PluginsDialog { Owner = this }.ShowDialog(); }

        private void NodeInput_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==Key.Enter) AddNodeInternal(NodeInput.Text); }
        private void GoldenKeyInput_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==Key.Enter) SaveGoldenKey_Click(s,null); }
        private void NodeList_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==Key.Delete) RemoveNode_Click(s,null); }
        private void OnCopyNodesClick(object s, RoutedEventArgs e) => CopySelectedNodesToClipboard();
        private void OnOpenNodeInBrowser(object s, RoutedEventArgs e) => NodeList_MouseDoubleClick(null, null);
        private void NodeList_MouseDoubleClick(object s, MouseButtonEventArgs e) { if(NodeList.SelectedItem!=null) Process.Start(new ProcessStartInfo($"https://funpay.com/lots/{NodeList.SelectedItem}/trade"){UseShellExecute=true}); }
        private void OnClosing(object s, System.ComponentModel.CancelEventArgs e) { _trayIcon.Dispose(); SaveStoreSync(); }
        
        private void Window_PreviewDragOver(object s, DragEventArgs e) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        private void Window_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.Text)) AddNodeInternal(e.Data.GetData(DataFormats.Text) as string); }
        private void NodeInput_Drop(object s, DragEventArgs e) => Window_Drop(s, e);
        private void GoldenKeyInput_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.Text)) GoldenKeyInput.Text = e.Data.GetData(DataFormats.Text) as string; }
        private void NodeList_Drop(object s, DragEventArgs e) => Window_Drop(s, e);

        private string Mask(string s) => s.Length <= 6 ? "***" : s.Substring(0,3)+"***"+s.Substring(s.Length-3);
        private void ShowThemed(string t, string m) => new UI.ThemedDialog(t, m) { Owner = this }.ShowDialog();
        private void CopySelectedNodesToClipboard() { try { Clipboard.SetText(string.Join("\n", NodeList.SelectedItems.Cast<object>())); AppendLog("[COPY] Скопировано"); } catch {} }
        
        private void OnAboutClick(object s, RoutedEventArgs e) => ShowThemed("О программе", "FPBooster v1.4");
        private void OnSupportClick(object s, RoutedEventArgs e) => ShowThemed("Поддержка", "@Manavoid_228");
        private void OnUpdatesClick(object s, RoutedEventArgs e) => ShowThemed("Обновления", "Версия актуальна");
        private void OnLicenseClick(object s, RoutedEventArgs e) => ShowThemed("Лицензия", "Лицензия активна");
        private void OnSettingsClick(object s, RoutedEventArgs e) => ShowThemed("Настройки", "В разработке");
        private void OnAuthorClick(object s, RoutedEventArgs e) => ShowThemed("Автор", "@Manavoid_228");
    }
}