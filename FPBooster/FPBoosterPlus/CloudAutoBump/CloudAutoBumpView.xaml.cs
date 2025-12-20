using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FPBooster.ServerApi;
using FPBooster.Config; 

// === УСТРАНЕНИЕ КОНФЛИКТОВ ИМЕН ===
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
// ==================================

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudAutoBumpView : UserControl
    {
        public event Action NavigateBack;

        private readonly CloudAutoBumpCore _core;
        
        // Лог. Private set позволяет заменить его через метод SetSharedLog
        public ObservableCollection<FPBooster.MainWindow.LogEntry> Logs { get; private set; } = new ObservableCollection<FPBooster.MainWindow.LogEntry>();
        
        private DispatcherTimer _refreshTimer;
        
        private DateTime? _serverNextBumpTime;
        private bool _isServerActive = false;
        private string _lastServerMessage = "";
        private bool _isUpdatingUi = false;

        // Флаг блокировки интерфейса (Cooldown)
        private bool _isCooldown = false;

        public CloudAutoBumpView()
        {
            InitializeComponent();
            _core = new CloudAutoBumpCore();
            LogList.ItemsSource = Logs;
            
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += OnTick;
            
            Loaded += async (s, e) => 
            { 
                LoadLocalConfig(); 
                TryFetchDataFromMainWindow();
                await SyncWithServer();
                _refreshTimer.Start(); 
            };
            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        // === МЕТОД ДЛЯ ОБЩЕГО ЛОГА ===
        public void SetSharedLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            Logs = sharedLog;
            LogList.ItemsSource = Logs;
        }
        // ==============================

        public void InitNodes(IEnumerable<string> nodes, string goldenKey) 
        {
            if (!string.IsNullOrEmpty(goldenKey)) InputKey.Password = goldenKey;
            if (nodes != null && nodes.Any()) InputNodes.Text = string.Join("\n", nodes);
        }

        // --- КНОПКА ИНФО ---
        private void OnInfoClick(object sender, RoutedEventArgs e)
        {
            new FPBooster.UI.ThemedDialog("Справка", 
                "Обновлять облачное авто-поднятие можно раз в 40 секунд, чтобы избежать лишней нагрузки на сервер FunPay и бана аккаунта.\n\n" +
                "Для корректной работы плагина постарайтесь не нажимать эти кнопки слишком часто.\n\n" +
                "Если нашли баг — сообщите разработчикам.")
            { Owner = Application.Current.MainWindow }.ShowDialog();
        }

        // --- ЛОГИКА БЛОКИРОВКИ КНОПОК (COOLDOWN) ---
        private async void StartUiCooldown()
        {
            if (_isCooldown) return;
            _isCooldown = true;

            BtnSave.IsEnabled = false;
            BtnRefresh.IsEnabled = false;
            SwitchActive.IsEnabled = false;

            var endTime = DateTime.Now.AddSeconds(40);

            while (DateTime.Now < endTime)
            {
                var remaining = (int)(endTime - DateTime.Now).TotalSeconds;
                if (remaining < 0) break;

                try
                {
                    BtnSave.Content = $"⏳ {remaining}с";
                    if (TxtRefresh != null) TxtRefresh.Text = $"{remaining}с";
                }
                catch { }

                await Task.Delay(1000);
            }

            try 
            {
                BtnSave.Content = "💾 СОХРАНИТЬ НА СЕРВЕРЕ";
                if (TxtRefresh != null) TxtRefresh.Text = "Обновить";

                BtnSave.IsEnabled = true;
                BtnRefresh.IsEnabled = true;
                SwitchActive.IsEnabled = true;
            }
            catch { }

            _isCooldown = false;
        }

        // --- КНОПКА СОХРАНИТЬ ---
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || _isCooldown) return;

            StartUiCooldown();
            Log("Bump: Сохранение...", Brushes.Gray);
            
            var key = InputKey.Password.Trim(); 
            var nodesText = InputNodes.Text;
            bool isActive = SwitchActive.IsChecked == true;

            SaveLocalConfig();

            var result = await _core.SaveSettingsAsync(key, nodesText, isActive);

            if (result.Success)
            {
                Log("Bump: ✅ " + result.Message, Brushes.LightGreen);
                await SyncWithServer();
            }
            else
            {
                Log("Bump: ❌ " + result.Message, Brushes.IndianRed);
                _isUpdatingUi = true;
                if (isActive) SwitchActive.IsChecked = false;
                _isUpdatingUi = false;
                UpdatePowerCardVisuals();
            }
        }

        // --- ТУМБЛЕР ---
        private void OnSwitchToggled(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _isUpdatingUi) 
            {
                UpdatePowerCardVisuals();
                return;
            }

            if (_isCooldown)
            {
                _isUpdatingUi = true;
                SwitchActive.IsChecked = !SwitchActive.IsChecked; 
                _isUpdatingUi = false;
                Log("⛔ Подождите окончания таймера!", Brushes.Tomato);
                return;
            }

            OnSaveClick(sender, e);
        }
        
        // --- КНОПКА ОБНОВИТЬ ---
        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (_isCooldown) return;

            StartUiCooldown();

            if (sender is Button btn && btn.Content is StackPanel sp && sp.Children[0] is TextBlock icon)
            {
                 var rotate = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1));
                 icon.RenderTransform = new RotateTransform(0, icon.ActualWidth/2, icon.ActualHeight/2);
                 icon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotate);
            }

            Log("🔄 Запрос серверу...", Brushes.Gray);
            
            var result = await _core.ForceRefreshAsync();
            
            if (result.Success)
            {
                Log("Bump: ✅ " + result.Message, Brushes.LightGreen);
                await SyncWithServer();
            }
            else
            {
                Log("Bump: ❌ " + result.Message, Brushes.IndianRed);
            }
        }

        // --- СТАНДАРТНЫЕ МЕТОДЫ ---
        private void LoadLocalConfig() { try { var cfg = ConfigManager.Load(); if (!string.IsNullOrWhiteSpace(cfg.GoldenKey)) InputKey.Password = cfg.GoldenKey; if (cfg.NodeIds != null && cfg.NodeIds.Any()) InputNodes.Text = string.Join("\n", cfg.NodeIds); } catch { } }
        private void SaveLocalConfig() { try { var cfg = ConfigManager.Load(); cfg.GoldenKey = InputKey.Password.Trim(); cfg.NodeIds = InputNodes.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList(); ConfigManager.Save(cfg); } catch { } }
        
        private int _tickCounter = 0;
        private async void OnTick(object sender, EventArgs e) 
        { 
            if (_tickCounter++ >= 10) { _tickCounter = 0; await SyncWithServer(); } 
            UpdateTimerText(); 
        }
        
        private async Task SyncWithServer() 
        { 
            var status = await _core.GetStatusAsync(); 
            _isServerActive = status.IsActive; 
            _serverNextBumpTime = status.NextRun; 
            _lastServerMessage = status.StatusText; 
            
            if (status.NodeIds != null && status.NodeIds.Count > 0 && string.IsNullOrWhiteSpace(InputNodes.Text)) 
            { 
                InputNodes.Text = string.Join("\n", status.NodeIds); 
                Log("📥 Лоты загружены с сервера", Brushes.Cyan); 
                SaveLocalConfig(); 
            } 
            
            _isUpdatingUi = true; 
            SwitchActive.IsChecked = _isServerActive; 
            _isUpdatingUi = false; 
            
            TxtStatus.Text = _isServerActive ? "АКТИВНО" : "ОСТАНОВЛЕНО"; 
            TxtStatus.Foreground = _isServerActive ? Brushes.SpringGreen : Brushes.Orange; 
            StatusIcon.Text = _isServerActive ? "▶" : "⏹"; 
            StatusIcon.Foreground = TxtStatus.Foreground; 
            
            int lotsCount = InputNodes.Text.Split(new[] {'\n', '\r'}, StringSplitOptions.RemoveEmptyEntries).Length; 
            TxtLotsCount.Text = $"{lotsCount} шт."; 
            
            UpdatePowerCardVisuals(); 
            
            if (!string.IsNullOrEmpty(_lastServerMessage) && (Logs.Count == 0 || !Logs[0].Text.Contains(_lastServerMessage))) 
            { 
                if (!_lastServerMessage.StartsWith("Ожидание") && !_lastServerMessage.StartsWith("В очереди")) 
                    Log("Cloud: " + _lastServerMessage, Brushes.LightBlue); 
            } 
        }
        
        private void UpdateTimerText() 
        { 
            if (!_isServerActive) { TxtNextRun.Text = "—"; return; } 
            if (_serverNextBumpTime.HasValue) 
            { 
                var diff = _serverNextBumpTime.Value.ToLocalTime() - DateTime.Now; 
                if (diff.TotalSeconds > 0) TxtNextRun.Text = diff.ToString(@"hh\:mm\:ss"); 
                else TxtNextRun.Text = "Запуск..."; 
            } 
            else { TxtNextRun.Text = "Ожидание..."; } 
        }
        
        private void TryFetchDataFromMainWindow() 
        { 
            try 
            { 
                var mainWindow = Application.Current.MainWindow; 
                if (mainWindow == null) return; 
                var type = mainWindow.GetType(); 
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance; 
                
                if (string.IsNullOrEmpty(InputKey.Password)) 
                { 
                    var fieldGkInput = type.GetField("GoldenKeyInput", flags); 
                    if (fieldGkInput != null && fieldGkInput.GetValue(mainWindow) is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) 
                        InputKey.Password = tb.Text.Trim(); 
                } 
                
                if (string.IsNullOrWhiteSpace(InputNodes.Text)) 
                { 
                    var fieldNodeList = type.GetField("NodeList", flags); 
                    if (fieldNodeList != null && fieldNodeList.GetValue(mainWindow) is ListBox lb && lb.Items.Count > 0) 
                    { 
                        var items = lb.Items.Cast<object>().Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)); 
                        InputNodes.Text = string.Join("\n", items); 
                    } 
                } 
            } 
            catch { } 
        }
        
        private void OnImportNodesClick(object s, RoutedEventArgs e) { TryFetchDataFromMainWindow(); }
        private void OnImportKeyClick(object s, RoutedEventArgs e) { TryFetchDataFromMainWindow(); }
        private void OnClearLogClick(object s, RoutedEventArgs e) => Logs.Clear();
        private void OnBackClick(object s, RoutedEventArgs e) => NavigateBack?.Invoke();
        
        private void UpdatePowerCardVisuals() 
        { 
            bool isRunning = SwitchActive.IsChecked == true; 
            ActiveStatusText.Text = isRunning ? "СЕРВЕР РАБОТАЕТ" : "СЕРВЕР ОСТАНОВЛЕН"; 
            ActiveStatusText.Foreground = isRunning ? Brushes.SpringGreen : Brushes.Gray; 
            PowerCardGlow.Opacity = isRunning ? 0.4 : 0.1; 
        }
        
        private void Log(string msg, Brush color) 
        { 
            Logs.Insert(0, new FPBooster.MainWindow.LogEntry { Text = $"[{DateTime.Now:HH:mm:ss}] {msg}", Color = color }); 
            if (Logs.Count > 100) Logs.RemoveAt(Logs.Count - 1); 
        }
    }
}