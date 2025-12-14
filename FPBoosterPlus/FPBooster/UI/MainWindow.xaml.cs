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
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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
using Button = System.Windows.Controls.Button; // <--- FIX CS0104
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
        private List<string> _cachedNodeIds = new();
        
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
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            LogBox.ItemsSource = _logEntries;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveTimer.Tick += async (s, e) => { _saveTimer.Stop(); await SaveStoreAsync(); };
            
            SizeChanged += (_, __) => { 
                if (ThemeParticles.Visibility == Visibility.Visible) 
                    ShowParticlesForTheme(ThemeManager.CurrentTheme);
            };

            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = false,
                Text = "FPBooster (Работает в фоне)"
            };
            _trayIcon.Click += (s, e) => RestoreFromEcoMode();

            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("Развернуть", null, (s, e) => RestoreFromEcoMode());
            trayMenu.Items.Add("Выход", null, (s, e) => { 
                _trayIcon.Visible = false; 
                Application.Current.Shutdown(); 
            });
            _trayIcon.ContextMenuStrip = trayMenu;

            if (ThemeCombo.Items.Count > 0) ThemeCombo.SelectedIndex = 0;
            SetupShortcuts();

            ThemeManager.BackgroundImageChanged += OnBackgroundImageChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void SetupShortcuts()
        {
            var copyCmd = new RoutedCommand();
            copyCmd.InputGestures.Add(new KeyGesture(Key.C, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(copyCmd, (_, __) => CopySelectedNodesToClipboard()));

            var selectAllCmd = new RoutedCommand();
            selectAllCmd.InputGestures.Add(new KeyGesture(Key.A, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(selectAllCmd, (_, __) => NodeList.SelectAll()));
        }

        private void OnThemeChanged(string newTheme)
        {
            foreach (ComboBoxItem item in ThemeCombo.Items) {
                if (item.Content.ToString()?.Replace(" ", "") == newTheme) {
                    ThemeCombo.SelectedItem = item; break;
                }
            }
            ThemeName.Text = (ThemeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? newTheme;
            ShowParticlesForTheme(newTheme);
        }

        // --- ВОССТАНОВЛЕННЫЙ МЕТОД (FIX CS1061) ---
        private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            var item = ThemeCombo.SelectedItem as ComboBoxItem; 
            if (item == null) return;
            var name = item.Content.ToString(); 
            ThemeManager.ApplyTheme(name); 
            TryPersistImmediate();
        }
        // ------------------------------------------

        private void OnBackgroundImageChanged(Uri imageUri)
        {
            var target = PluginArea.Visibility == Visibility.Visible ? PluginBannerImage : RightBackgroundImage;
            var fadeOut = new DoubleAnimation(target.Opacity, 0, TimeSpan.FromMilliseconds(500));
            fadeOut.Completed += (_, __) =>
            {
                try {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.UriSource = imageUri; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    target.Source = bmp;
                    target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.25, TimeSpan.FromMilliseconds(500)));
                } catch { }
            };
            target.BeginAnimation(OpacityProperty, fadeOut);
        }

        public string GetGoldenKey() => GoldenKeyInput.Text?.Trim() ?? "";
        
        public List<string> GetActiveNodeIds() => NodeList.Items.Cast<object>()
                .Select(i => i?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        public ObservableCollection<LogEntry> GetLogCollection() => _logEntries;
        public string GetCurrentTheme() => ThemeManager.CurrentTheme;
        public void Log(string msg) => AppendLog(msg);

        private async void OnExtractNodesClick(object sender, RoutedEventArgs e)
        {
            var key = GoldenKeyInput.Text?.Trim();
            if (string.IsNullOrEmpty(key)) { ShowThemed("Ошибка", "Сначала введите и сохраните Golden Key!"); return; }

            var btn = sender as Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳"; }

            AppendLog("[INFO] Начинаю поиск разделов (NodeID)...");
            try 
            {
                var client = AutoBumpCore.CreateClientWithCookie(key);
                var userId = await GetUserIdAsync(client);
                if (string.IsNullOrEmpty(userId)) { AppendLog("[ERR] Не удалось определить UserID."); return; }

                AppendLog($"[INFO] ID пользователя: {userId}. Сканирую профиль...");
                var nodes = await GetActiveNodeIdsFromProfileAsync(client, userId);

                if (nodes.Count == 0) { AppendLog("[WARN] Активных разделов не найдено."); return; }

                int added = 0;
                foreach (var nid in nodes) {
                    bool exists = false;
                    foreach (var item in NodeList.Items) if (item.ToString() == nid) { exists = true; break; }
                    if (!exists) { NodeList.Items.Add(nid); added++; }
                }

                NodeCount.Text = NodeList.Items.Count.ToString();
                AppendLog($"[SUCCESS] Добавлено новых разделов: {added}");
                if (added > 0) TryPersistImmediate();
            }
            catch (Exception ex) { AppendLog($"[ERR] Ошибка поиска: {ex.Message}"); }
            finally { if (btn != null) { btn.IsEnabled = true; btn.Content = "🔄"; } }
        }

        private async Task<string?> GetUserIdAsync(HttpClient client)
        {
            try {
                var resp = await client.GetAsync("https://funpay.com/");
                if (!resp.IsSuccessStatusCode) return null;
                var html = await resp.Content.ReadAsStringAsync();
                var m = Regex.Match(html, @"data-app-data=""([^""]+)""");
                if (!m.Success) return null;
                var blob = WebUtility.HtmlDecode(m.Groups[1].Value);
                var m2 = Regex.Match(blob, @"""userId""\s*:\s*([0-9]+)");
                return m2.Success ? m2.Groups[1].Value : null;
            } catch { return null; }
        }

        private async Task<List<string>> GetActiveNodeIdsFromProfileAsync(HttpClient client, string userId)
        {
            var list = new List<string>();
            try {
                var url = $"https://funpay.com/users/{userId}/";
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return list;
                var html = await resp.Content.ReadAsStringAsync();
                
                var matches = Regex.Matches(html, @"/lots/(\d+)/trade");
                foreach (Match m in matches)
                {
                    if (m.Success)
                    {
                        var id = m.Groups[1].Value;
                        if (!list.Contains(id)) list.Add(id);
                    }
                }
            } catch { }
            return list;
        }

        public void SwitchToPluginView(object viewContent, string pluginName)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, __) =>
            {
                DashboardGrid.Visibility = Visibility.Collapsed;
                PluginHost.Content = viewContent;
                PluginArea.Opacity = 0;
                PluginArea.Visibility = Visibility.Visible;

                if (ThemeManager.CurrentTheme == "Celestial") {
                    ThemeParticles.Visibility = Visibility.Collapsed;
                    PluginParticles.Visibility = Visibility.Visible;
                }

                var scale = new ScaleTransform(0.95, 0.95);
                PluginArea.RenderTransform = scale;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                var scaleIn = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                PluginArea.BeginAnimation(OpacityProperty, fadeIn);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            };
            DashboardGrid.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void OnBackFromPlugin_Click(object sender, RoutedEventArgs e)
        {
            var scale = new ScaleTransform(1, 1);
            PluginArea.RenderTransform = scale;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            var scaleOut = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, __) =>
            {
                PluginArea.Visibility = Visibility.Collapsed;
                PluginHost.Content = null;
                DashboardGrid.Opacity = 0;
                DashboardGrid.Visibility = Visibility.Visible;

                if (ThemeManager.CurrentTheme == "Celestial") {
                    ThemeParticles.Visibility = Visibility.Visible;
                    PluginParticles.Visibility = Visibility.Collapsed;
                }

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                var scaleIn = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                DashboardGrid.RenderTransform = new ScaleTransform(0.95, 0.95);
                DashboardGrid.BeginAnimation(OpacityProperty, fadeIn);
                DashboardGrid.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                DashboardGrid.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            };
            PluginArea.BeginAnimation(OpacityProperty, fadeOut);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        }

        private void OpenAutoBump_Click(object sender, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "auto_bump");
        private void OpenLotsToggle_Click(object sender, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "lots_toggle");
        private void OpenLotsDelete_Click(object sender, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "lots_delete");
        private void OpenAutoRestock_Click(object sender, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "auto_restock");
        private void OpenAdvProfileStat_Click(object sender, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "adv_profile_stat");
        private void OpenPlusCloud_Click(object sender, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "fp_plus_dashboard");
        private void OnMoreButtonClick(object sender, RoutedEventArgs e) { var dlg = new UI.PluginsDialog { Owner = this }; dlg.ShowDialog(); }

        private void AppendLog(string msg)
        {
            if (_logEntries.Count > 200) _logEntries.RemoveAt(0);
            Brush color = Brushes.White;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[WARN]")) color = Brushes.Goldenrod;
            else if (msg.Contains("[INFO]")) color = Brushes.LightGreen;
            else if (msg.Contains("[ECO]")) color = Brushes.LimeGreen;
            else if (msg.Contains("[ADD]")) color = Brushes.LightSkyBlue;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.Lime;
            else if (msg.Contains("[COPY]")) color = Brushes.LightCyan;

            if (color.CanFreeze) color.Freeze();
            _logEntries.Add(new LogEntry { Text = msg, Color = color });
            
            Dispatcher.InvokeAsync(() => {
                try {
                    DependencyObject obj = LogBox;
                    while (obj != null && !(obj is ScrollViewer)) obj = VisualTreeHelper.GetParent(obj);
                    if (obj is ScrollViewer sv) sv.ScrollToBottom();
                } catch {}
            });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) { _logEntries.Clear(); AppendLog("[CLR] Лог очищён"); }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try {
                if (CloudApiClient.Instance.TryLoadToken()) AppendLog("[INFO] Авторизация успешна (Cloud)");
                _isLoaded = false;
                _config = FPBooster.Config.ConfigManager.Load();
                ThemeManager.ApplyTheme(!string.IsNullOrEmpty(_config.Theme) ? _config.Theme : "Midnight Blue");
                _cachedGoldenKey = _config.GoldenKey ?? "";
                GoldenKeyInput.Text = _cachedGoldenKey;
                GoldenKeyMasked.Text = string.IsNullOrWhiteSpace(_cachedGoldenKey) ? "—" : Mask(_cachedGoldenKey);
                _cachedUserName = _config.UserName ?? "";
                if (!string.IsNullOrWhiteSpace(_cachedUserName)) LicenseStatus.Text = $"Аккаунт: {_cachedUserName}";
                _cachedNodeIds = (_config.NodeIds ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
                NodeList.Items.Clear();
                foreach (var nid in _cachedNodeIds) NodeList.Items.Add(nid);
                NodeCount.Text = NodeList.Items.Count.ToString();
                _isLoaded = true;
                Dispatcher.BeginInvoke(new Action(() => ShowParticlesForTheme(ThemeManager.CurrentTheme)), DispatcherPriority.ApplicationIdle);
                AppendLog("[INFO] Интерфейс загружен");
            } catch (Exception ex) { _isLoaded = true; AppendLog($"[ERR] {ex.Message}"); }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            SaveStoreSync();
        }

        public void SaveStore() => SaveStoreSync();
        private async Task SaveStoreAsync() { if (!_isLoaded) return; var cfg = GetConfigFromUI(); await Task.Run(() => { try { FPBooster.Config.ConfigManager.Save(cfg); } catch { } }); }
        private void SaveStoreSync() { if (!_isLoaded) return; try { FPBooster.Config.ConfigManager.Save(GetConfigFromUI()); } catch { } }
        private FPBooster.Config.ConfigManager.ConfigData GetConfigFromUI()
        {
            return new FPBooster.Config.ConfigManager.ConfigData {
                GoldenKey = GoldenKeyInput.Text?.Trim() ?? "",
                NodeIds = NodeList.Items.Cast<object>().Select(i => i?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList(),
                Theme = (ThemeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Midnight Blue",
                UserName = _cachedUserName,
                LicenseStatus = LicenseStatus.Text
            };
        }
        private void TryPersistImmediate() { _saveTimer.Stop(); _saveTimer.Start(); }

        private void ShowParticlesForTheme(string themeKey)
        {
            ThemeParticles.Children.Clear(); PluginParticles.Children.Clear();
            if (themeKey.Replace(" ", "") != "Celestial") { ThemeParticles.Visibility = Visibility.Collapsed; PluginParticles.Visibility = Visibility.Collapsed; return; }
            CreateParticlesForCanvas(ThemeParticles, ActualWidth, ActualHeight);
            ThemeParticles.Visibility = Visibility.Visible;
            CreateParticlesForCanvas(PluginParticles, ActualWidth, ActualHeight);
            PluginParticles.Visibility = PluginArea.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CreateParticlesForCanvas(Canvas canvas, double w, double h)
        {
            if (canvas == null) return;
            int count = 35; var brush = (Brush)FindResource("BrushAccentLight"); if (brush.CanFreeze) brush.Freeze();
            for (int i = 0; i < count; i++) {
                var size = _rng.Next(1, 3);
                var star = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = brush, Opacity = _rng.NextDouble() * 0.6 + 0.2, IsHitTestVisible = false };
                Canvas.SetLeft(star, _rng.NextDouble() * (w<=0?1200:w)); Canvas.SetTop(star, _rng.NextDouble() * (h<=0?800:h));
                canvas.Children.Add(star);
                var anim = new DoubleAnimation { To = Math.Min(1.0, star.Opacity + 0.4), Duration = TimeSpan.FromSeconds(_rng.Next(2,5)), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                if (anim.CanFreeze) anim.Freeze(); star.BeginAnimation(OpacityProperty, anim);
            }
        }

        private void AddNode_Click(object sender, RoutedEventArgs e) => AddNodeInternal(NodeInput.Text);
        
        private void AddNodeInternal(string? text) {
            var t = text ?? ""; 
            var nid = ResolveNodeId(t);
            
            if (string.IsNullOrEmpty(nid) || !Regex.IsMatch(nid, @"^\d+$")) { 
                ShowThemed("Ошибка", "Некорректный ID раздела!"); 
                return; 
            }
            
            foreach (var item in NodeList.Items) if (item.ToString() == nid) return;
            NodeList.Items.Add(nid); 
            NodeCount.Text = NodeList.Items.Count.ToString();
            AppendLog($"[ADD] Раздел добавлен: {nid}"); 
            NodeInput.Clear(); 
            TryPersistImmediate();
        }
        
        private void RemoveNode_Click(object sender, RoutedEventArgs e) {
            var selected = new List<object>(NodeList.SelectedItems.Cast<object>());
            foreach (var s in selected) NodeList.Items.Remove(s);
            NodeCount.Text = NodeList.Items.Count.ToString(); TryPersistImmediate();
        }
        private void ClearNodes_Click(object sender, RoutedEventArgs e) { NodeList.Items.Clear(); NodeCount.Text="0"; TryPersistImmediate(); }
        
        private async void SaveGoldenKey_Click(object sender, RoutedEventArgs e) {
            var k = GoldenKeyInput.Text?.Trim(); 
            if (string.IsNullOrEmpty(k)) { ShowThemed("Ошибка", "Введите ключ!"); return; }
            
            // Исправлен конфликт с Button и CS0136
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            
            try 
            {
                var client = AutoBumpCore.CreateClientWithCookie(k);
                var (ok, user) = await Csrf.VerifyGoldenKeyAsync(client);
                
                if (ok) { 
                    _cachedUserName = user ?? "User"; 
                    LicenseStatus.Text = $"Аккаунт: {_cachedUserName}"; 
                    GoldenKeyMasked.Text = Mask(k); 
                    ShowThemed("Успех", $"Ключ валиден!\nАккаунт: {_cachedUserName}"); 
                    TryPersistImmediate(); 
                }
                else 
                { 
                    ShowThemed("Ошибка", "Невалидный Golden Key!"); 
                }
            }
            catch (Exception ex) 
            {
                ShowThemed("Ошибка", $"Сбой проверки: {ex.Message}");
            }
            finally 
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void OnEcoModeClick(object sender, RoutedEventArgs e)
        {
            string title = "Режим 'Eco Mode' (Фоновая работа)";
            string description = 
                "Этот режим оптимизирует работу программы для слабых ПК или во время игр.\n\n" +
                "✅ Что произойдет:\n" +
                "• Интерфейс будет скрыт, программа свернется в трей (значок возле часов).\n" +
                "• Отключатся все визуальные эффекты и анимации.\n" +
                "• Потребление оперативной памяти (RAM) будет сжато до минимума.\n" +
                "• Нагрузка на процессор (CPU) упадет почти до 0%.\n\n" +
                "⚡ Важно: Все запущенные задачи (Авто-поднятие, Автовыдача) ПРОДОЛЖАТ работать в штатном режиме!\n\n" +
                "Перейти в этот режим?";

            var result = MessageBox.Show(description, title, MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes) EnterEcoMode();
        }

        private void EnterEcoMode()
        {
            _inEcoMode = true;
            this.Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(3000, "FPBooster", "Софт работает в Демо-режиме (Eco)", WinForms.ToolTipIcon.Info);

            ThemeParticles.Children.Clear();
            PluginParticles.Children.Clear();
            
            try {
                using (Process p = Process.GetCurrentProcess())
                    p.PriorityClass = ProcessPriorityClass.Idle;
            } catch { }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            try {
                using (Process p = Process.GetCurrentProcess())
                    SetProcessWorkingSetSize(p.Handle, -1, -1);
            } catch { }
            
            Log("[ECO] Демо-режим включен. Работаем в фоне.");
        }

        private void RestoreFromEcoMode()
        {
            _inEcoMode = false;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            _trayIcon.Visible = false;

            try {
                using (Process p = Process.GetCurrentProcess())
                    p.PriorityClass = ProcessPriorityClass.Normal;
            } catch { }

            ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);
            Log("[ECO] Интерфейс восстановлен.");
        }

        private void OnAboutClick(object sender, RoutedEventArgs e) => ShowThemed("О программе", "FPBooster v1.4");
        private void OnSupportClick(object sender, RoutedEventArgs e) => ShowThemed("Поддержка", "@Manavoid_228");
        private void OnUpdatesClick(object sender, RoutedEventArgs e) => ShowThemed("Обновления", "Версия актуальна");
        private void OnLicenseClick(object sender, RoutedEventArgs e) => ShowThemed("Лицензия", "Лицензия активна");
        private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowThemed("Настройки", "В разработке");
        private void OnAuthorClick(object sender, RoutedEventArgs e) => ShowThemed("Автор", "@Manavoid_228");
        private string ResolveNodeId(string t) { t = (t??"").Trim(); if (Regex.IsMatch(t, @"^\d+$")) return t; var m = Regex.Match(t, @"/lots/(\d+)/"); return m.Success ? m.Groups[1].Value : ""; }
        private string Mask(string s) => s.Length <= 6 ? "***" : s.Substring(0,3) + "***" + s.Substring(s.Length-3);
        private void ShowThemed(string t, string m) { new UI.ThemedDialog(t, m) { Owner = this }.ShowDialog(); }
        private void CopySelectedNodesToClipboard() {
            var selected = NodeList.SelectedItems.Cast<object>().Select(i => i?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (selected.Count == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, selected)); AppendLog($"[COPY] В буфер обмена: {selected.Count} NodeID"); } catch (Exception ex) { AppendLog($"[WARN] {ex.Message}"); }
        }
        private void Window_PreviewDragOver(object s, DragEventArgs e) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        private void Window_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.Text)) AddNodeInternal(e.Data.GetData(DataFormats.Text) as string); }
        private void NodeList_MouseDoubleClick(object s, MouseButtonEventArgs e) { if(NodeList.SelectedItem != null) try { Process.Start(new ProcessStartInfo($"https://funpay.com/lots/{NodeList.SelectedItem}/trade") { UseShellExecute = true }); } catch {} }
        private void OnCopyNodesClick(object s, RoutedEventArgs e) => CopySelectedNodesToClipboard();
        private void OnOpenNodeInBrowser(object s, RoutedEventArgs e) => NodeList_MouseDoubleClick(null, null);
        private void NodeInput_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==Key.Enter) AddNode_Click(s,null); }
        private void GoldenKeyInput_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==Key.Enter) SaveGoldenKey_Click(s,null); }
        private void NodeList_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==Key.Delete) RemoveNode_Click(s,null); }
        private void NodeInput_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.Text)) NodeInput.Text = e.Data.GetData(DataFormats.Text) as string; }
        private void GoldenKeyInput_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.Text)) GoldenKeyInput.Text = e.Data.GetData(DataFormats.Text) as string; }
        private void NodeList_Drop(object s, DragEventArgs e) { Window_Drop(s, e); }
    }
}