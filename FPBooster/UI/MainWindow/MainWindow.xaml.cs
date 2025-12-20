using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using FPBooster.ServerApi;
using FPBooster.UI;

// Псевдонимы
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataFormats = System.Windows.DataFormats;

namespace FPBooster
{
    public partial class MainWindow : Window
    {
        public class LogEntry { public required string Text { get; set; } public required Brush Color { get; set; } }

        private FPBooster.Config.ConfigManager.ConfigData _config = new();
        private readonly ObservableCollection<LogEntry> _logEntries = new();

        private string _cachedGoldenKey = "";
        private string _cachedUserName = "";
        private bool _isKeyValid = false; 
        private bool _isLoaded = false;
        private readonly Random _rng = new();
        private DispatcherTimer _saveTimer;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private bool _inEcoMode = false;

        public MainWindow()
        {
            InitializeComponent();
            
            System.Windows.Media.TextOptions.SetTextFormattingMode(this, System.Windows.Media.TextFormattingMode.Display);
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(this, System.Windows.Media.BitmapScalingMode.LowQuality);
            
            LogBox.ItemsSource = _logEntries;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveTimer.Tick += async (s, e) => { _saveTimer.Stop(); await SaveStoreAsync(); };
            
            SizeChanged += (_, __) => { 
                if (ThemeParticles.Visibility == Visibility.Visible) 
                    ShowParticlesForTheme(ThemeManager.CurrentTheme);
            };

            InitTray();
            InitInfoSlideshow(); // Запуск слайд-шоу

            if (ThemeCombo.Items.Count > 0) ThemeCombo.SelectedIndex = 0;
            
            var copyCmd = new System.Windows.Input.RoutedCommand(); 
            copyCmd.InputGestures.Add(new System.Windows.Input.KeyGesture(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control));
            CommandBindings.Add(new System.Windows.Input.CommandBinding(copyCmd, (_, __) => CopySelectedNodesToClipboard()));
            
            var selectAllCmd = new System.Windows.Input.RoutedCommand(); 
            selectAllCmd.InputGestures.Add(new System.Windows.Input.KeyGesture(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Control));
            CommandBindings.Add(new System.Windows.Input.CommandBinding(selectAllCmd, (_, __) => NodeList.SelectAll()));

            ThemeManager.BackgroundImageChanged += OnBackgroundImageChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;

            SetNodesInputEnabled(false);
        }

        private void OnToggleInfoClick(object sender, RoutedEventArgs e)
        {
            if (InfoDetails.Visibility == Visibility.Visible)
            {
                InfoDetails.Visibility = Visibility.Collapsed;
                InfoSlideshowImage.Visibility = Visibility.Visible;
                BtnToggleInfo.Content = "📖 Читать инструкцию";
            }
            else
            {
                InfoDetails.Visibility = Visibility.Visible;
                InfoSlideshowImage.Visibility = Visibility.Collapsed;
                BtnToggleInfo.Content = "❌ Скрыть инструкцию";
            }
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

        public void Log(string msg) => AppendLog(msg);
        public ObservableCollection<LogEntry> GetLogCollection() => _logEntries;
        
        // --- ОБНОВЛЕННЫЙ МЕТОД ЛОГИРОВАНИЯ (АВТО-ПОКРАСКА) ---
        public void AppendLog(string msg, Brush? color = null)
        {
            if (_logEntries.Count > 200) _logEntries.RemoveAt(0);

            // Если цвет не передан, определяем его по тегу в тексте
            if (color == null)
            {
                if (msg.Contains("[ERR]") || msg.Contains("Ошибка") || msg.Contains("Fail")) color = Brushes.IndianRed;
                else if (msg.Contains("[WARN]")) color = Brushes.Orange;
                else if (msg.Contains("[SUCCESS]")) color = Brushes.SpringGreen;
                else if (msg.Contains("[AUTH]")) color = Brushes.LimeGreen;
                else if (msg.Contains("[INFO]")) color = Brushes.SkyBlue;
                else if (msg.Contains("[ECO]")) color = Brushes.MediumSpringGreen;
                else if (msg.Contains("[PLUGIN]")) color = Brushes.MediumPurple;
                else if (msg.Contains("[COPY]")) color = Brushes.CornflowerBlue;
                else if (msg.Contains("[ADD]")) color = Brushes.Gold;
                else color = Brushes.WhiteSmoke; // Стандартный цвет чуть мягче белого
            }

            var c = color;
            if (c.CanFreeze) c.Freeze();
            _logEntries.Add(new LogEntry { Text = msg, Color = c });
            
            Dispatcher.InvokeAsync(() => { 
                try { 
                    if (System.Windows.Media.VisualTreeHelper.GetChild(LogBox, 0) is Decorator border && border.Child is ScrollViewer sv) sv.ScrollToBottom(); 
                } catch {} 
            });
        }
        // ----------------------------------------------------

        private void ClearLog_Click(object sender, RoutedEventArgs e) { _logEntries.Clear(); AppendLog("[CLR] Очищено", Brushes.Gray); }
        private void RemoveNode_Click(object sender, RoutedEventArgs e) { var selected = new List<object>(NodeList.SelectedItems.Cast<object>()); foreach (var s in selected) NodeList.Items.Remove(s); NodeCount.Text = NodeList.Items.Count.ToString(); TryPersistImmediate(); }
        private void ClearNodes_Click(object sender, RoutedEventArgs e) { NodeList.Items.Clear(); NodeCount.Text="0"; TryPersistImmediate(); }
        
        private void NodeInput_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==System.Windows.Input.Key.Enter) AddNodeInternal(NodeInput.Text); }
        private void GoldenKeyInput_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==System.Windows.Input.Key.Enter) SaveGoldenKey_Click(s,null); }
        private void NodeList_PreviewKeyDown(object s, KeyEventArgs e) { if(e.Key==System.Windows.Input.Key.Delete) RemoveNode_Click(s,null); }
        
        private void OnCopyNodesClick(object s, RoutedEventArgs e) => CopySelectedNodesToClipboard();
        private void OnOpenNodeInBrowser(object s, RoutedEventArgs e) => NodeList_MouseDoubleClick(null, null);
        
        private void NodeList_MouseDoubleClick(object s, MouseButtonEventArgs e) 
        { 
            if(NodeList.SelectedItem!=null) 
                try { 
                    Process.Start(new ProcessStartInfo($"https://funpay.com/lots/{NodeList.SelectedItem}/trade"){UseShellExecute=true}); 
                } catch {} 
        }
        
        private void OnClosing(object s, System.ComponentModel.CancelEventArgs e) { _trayIcon.Dispose(); SaveStoreSync(); }
        
        private void Window_PreviewDragOver(object s, DragEventArgs e) { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; }
        
        private void Window_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(System.Windows.DataFormats.Text)) AddNodeInternal(e.Data.GetData(System.Windows.DataFormats.Text) as string); }
        
        private void NodeInput_Drop(object s, DragEventArgs e) => Window_Drop(s, e);
        private void GoldenKeyInput_Drop(object s, DragEventArgs e) { if(e.Data.GetDataPresent(System.Windows.DataFormats.Text)) GoldenKeyInput.Text = e.Data.GetData(System.Windows.DataFormats.Text) as string; }
        private void NodeList_Drop(object s, DragEventArgs e) => Window_Drop(s, e);

        private void OnAboutClick(object s, RoutedEventArgs e) => ShowThemed("О программе", "FPBooster v1.4");
        private void OnSupportClick(object s, RoutedEventArgs e) => ShowThemed("Поддержка", "@Manavoid_228");
        private void OnUpdatesClick(object s, RoutedEventArgs e) => ShowThemed("Обновления", "Версия актуальна");
        private void OnLicenseClick(object s, RoutedEventArgs e) => ShowThemed("Лицензия", "Лицензия активна");
        private void OnSettingsClick(object s, RoutedEventArgs e) => ShowThemed("Настройки", "В разработке");
        private void OnAuthorClick(object s, RoutedEventArgs e) => ShowThemed("Автор", "@Manavoid_228");

        private void OpenAutoBump_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "auto_bump");
        private void OpenLotsToggle_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "lots_toggle");
        private void OpenLotsDelete_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "lots_delete");
        private void OpenAutoRestock_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "auto_restock");
        private void OpenAdvProfileStat_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "adv_profile_stat");
        private void OpenPlusCloud_Click(object s, RoutedEventArgs e) => UI.PluginsDialog.RunPlugin(this, "fp_plus_dashboard");
        private void OnMoreButtonClick(object s, RoutedEventArgs e) { new UI.PluginsDialog { Owner = this }.ShowDialog(); }
    }
}