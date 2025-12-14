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

// Псевдонимы
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace FPBooster.Plugins
{
    public partial class AutoBumpView : UserControl, IPlugin
    {
        private readonly AutoBumpCore _core;
        private string _goldenKey = "";
        private DispatcherTimer _autoBumpTimer;
        private bool _isInitialized = false;
        private bool _isAutoBumpRunning = false; 
        private bool _isQuickBumpRunning = false;
        private int _totalAttempts = 0;
        private int _successfulBumps = 0;
        private Random _random = new Random();

        public string Id => "auto_bump";
        public string DisplayName => "Авто поднятие лотов";

        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private ObservableCollection<string> _nodes = new ObservableCollection<string>();

        public AutoBumpView()
        {
            InitializeComponent();
            _core = new AutoBumpCore(new System.Net.Http.HttpClient());
            _autoBumpTimer = new DispatcherTimer();
            _autoBumpTimer.Tick += async (s, e) => await RunAutoBumpCycle();
            
            Loaded += (s, e) => {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        public UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
            _nodes.Clear();
            foreach (var n in nodes) _nodes.Add(n);
            
            // Исправленный доступ к TextBox
            if (this.FindName("NodesPreview") is TextBox tb)
                tb.Text = string.Join(", ", _nodes);

            var client = AutoBumpCore.CreateClientWithCookie(_goldenKey);
            _core.SetHttpClient(client);

            if (!_isInitialized)
            {
                AppendPluginLog($"[INIT] Плагин загружен. Лотов: {_nodes.Count}");
                _isInitialized = true;
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
            PluginLog.ItemsSource = _sharedLog;
        }

        public void SetTheme(string themeKey) { }

        // --- HANDLERS ---
        private void OnToggleBumpClick(object sender, RoutedEventArgs e)
        {
            if (_isAutoBumpRunning) {
                _isAutoBumpRunning = false;
                _autoBumpTimer.Stop();
                BtnToggle.Content = "▶ ЗАПУСТИТЬ ЦИКЛ";
                if (TryFindResource("PluginPrimaryButton") is Style s) BtnToggle.Style = s;
                BtnQuick.IsEnabled = true;
                StatusText.Text = "ОСТАНОВЛЕНО";
                StatusText.Foreground = Brushes.IndianRed;
                AppendPluginLog("[INFO] Цикл остановлен.");
            } else {
                if (!CheckRequirements()) return;
                _isAutoBumpRunning = true;
                BtnToggle.Content = "⏹ ОСТАНОВИТЬ";
                BtnQuick.IsEnabled = false;
                StatusText.Text = "АКТИВНО (LOCAL)";
                StatusText.Foreground = Brushes.LightGreen;
                AppendPluginLog("[INFO] Цикл запущен.");
                _ = RunAutoBumpCycle(); 
            }
        }

        private async void OnQuickCheckClick(object sender, RoutedEventArgs e)
        {
            if (_isQuickBumpRunning) return;
            if (!CheckRequirements()) return;
            _isQuickBumpRunning = true;
            BtnQuick.IsEnabled = false;
            BtnToggle.IsEnabled = false;
            AppendPluginLog("[INFO] >> Разовая проверка...");
            await RunAutoBumpCycle();
            AppendPluginLog("[INFO] << Проверка завершена.");
            BtnQuick.IsEnabled = true;
            BtnToggle.IsEnabled = true;
            _isQuickBumpRunning = false;
        }

        private bool CheckRequirements()
        {
            if (_nodes.Count == 0) { MessageBox.Show("Нет лотов!"); return false; }
            if (string.IsNullOrEmpty(_goldenKey)) { MessageBox.Show("Нет ключа!"); return false; }
            return true;
        }

        private async Task RunAutoBumpCycle()
        {
            _autoBumpTimer.Stop();
            int raised = 0;
            foreach (var node in _nodes)
            {
                if (!_isAutoBumpRunning && !_isQuickBumpRunning) break;
                var (csrf, gameId, error) = await _core.GetLotInfoAsync(node);
                if (!string.IsNullOrEmpty(error)) {
                    if (error.Contains("Таймер")) AppendPluginLog($"[WAIT] Лот {node}: {error}");
                    else AppendPluginLog($"[ERR] Лот {node}: {error}");
                }
                else if (!string.IsNullOrEmpty(gameId)) {
                    await Task.Delay(_random.Next(2000, 4000));
                    var (ok, msg) = await _core.BumpLotPostAsync(node, gameId, csrf ?? "");
                    _totalAttempts++;
                    if (ok) { raised++; _successfulBumps++; AppendPluginLog($"[SUCCESS] Лот {node}: {msg}"); }
                    else AppendPluginLog($"[FP] Лот {node}: {msg}");
                } else AppendPluginLog($"[SKIP] Лот {node}: Кнопка не найдена.");
                
                UpdateStats();
                await Task.Delay(_random.Next(4000, 8000));
            }
            if (raised > 0) AppendPluginLog($"[RESULT] Поднято: {raised}");
            
            if (_isAutoBumpRunning) {
                int h = 4, j = 15;
                if (int.TryParse(IntervalInput.Text, out int hh)) h = hh;
                if (int.TryParse(JitterInput.Text, out int jj)) j = jj;
                var delay = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(_random.Next(0, j));
                _autoBumpTimer.Interval = delay;
                _autoBumpTimer.Start();
                StatusText.Text = $"След: {DateTime.Now + delay:HH:mm}";
            } else StatusText.Text = "ГОТОВ";
        }

        private void UpdateStats() { TxtTotal.Text = _totalAttempts.ToString(); TxtSuccess.Text = _successfulBumps.ToString(); }
        private void AppendPluginLog(string msg) {
            if (_sharedLog == null) return;
            Brush color = Brushes.White;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.LightGreen;
            _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = $"{DateTime.Now:HH:mm:ss} {msg}", Color = color });
            try { if (PluginLog.Items.Count > 0) PluginLog.ScrollIntoView(PluginLog.Items[PluginLog.Items.Count - 1]); } catch {}
        }
        private void OnClearPluginLog(object sender, RoutedEventArgs e) => _sharedLog?.Clear();
    }
}