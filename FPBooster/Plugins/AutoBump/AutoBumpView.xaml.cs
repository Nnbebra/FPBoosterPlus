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

// Псевдонимы для устранения конфликтов
using UserControl = System.Windows.Controls.UserControl;
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
            
            Loaded += (s, e) => 
            {
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
            
            // Заполнение поля предпросмотра (если оно есть в XAML)
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

        // --- ЛОГИКА ---

        private void OnToggleBumpClick(object sender, RoutedEventArgs e)
        {
            if (_isAutoBumpRunning)
            {
                _isAutoBumpRunning = false;
                _autoBumpTimer.Stop();
                
                BtnToggle.Content = "▶ ЗАПУСТИТЬ ЦИКЛ";
                BtnToggle.Style = (Style)FindResource("PluginPrimaryButton");
                BtnQuick.IsEnabled = true;
                
                StatusText.Text = "ОСТАНОВЛЕНО";
                StatusText.Foreground = Brushes.IndianRed;
                AppendPluginLog("[INFO] Цикл авто-поднятия остановлен.");
            }
            else
            {
                if (!CheckRequirements()) return;

                _isAutoBumpRunning = true;
                BtnToggle.Content = "⏹ ОСТАНОВИТЬ";
                BtnQuick.IsEnabled = false;

                StatusText.Text = "АКТИВНО (LOCAL)";
                StatusText.Foreground = Brushes.LightGreen;
                
                AppendPluginLog("[INFO] Цикл авто-поднятия запущен.");
                _ = RunAutoBumpCycle(); 
            }
        }

        private async void OnQuickCheckClick(object sender, RoutedEventArgs e)
        {
            if (_isQuickBumpRunning) return;
            if (!CheckRequirements()) return;

            _isQuickBumpRunning = true;
            BtnQuick.IsEnabled = false;
            BtnQuick.Content = "⏳ Работаем...";
            BtnToggle.IsEnabled = false;
            
            AppendPluginLog("[INFO] >> Запущена разовая проверка...");
            await RunAutoBumpCycle();
            AppendPluginLog("[INFO] << Разовая проверка завершена.");
            
            BtnQuick.Content = "⚡ Разовая проверка";
            BtnQuick.IsEnabled = true;
            BtnToggle.IsEnabled = true;
            _isQuickBumpRunning = false;
        }

        private bool CheckRequirements()
        {
            if (_nodes.Count == 0)
            {
                MessageBox.Show("Список лотов пуст!\nДобавьте ID лотов в главном меню.", "Ошибка");
                AppendPluginLog("[ERR] Нет лотов для проверки.");
                return false;
            }
            if (string.IsNullOrEmpty(_goldenKey))
            {
                MessageBox.Show("Не введен Golden Key!", "Ошибка");
                return false;
            }
            return true;
        }

        private async Task RunAutoBumpCycle()
        {
            _autoBumpTimer.Stop();
            int raised = 0;
            
            foreach (var node in _nodes)
            {
                if (!_isAutoBumpRunning && !_isQuickBumpRunning) 
                {
                    AppendPluginLog("[INFO] Прервано пользователем.");
                    break;
                }

                var (csrf, gameId, error) = await _core.GetLotInfoAsync(node);

                if (!string.IsNullOrEmpty(error))
                {
                    if (error.Contains("Таймер")) AppendPluginLog($"[WAIT] Лот {node}: {error}");
                    else AppendPluginLog($"[ERR] Лот {node}: {error}");
                }
                else if (!string.IsNullOrEmpty(gameId))
                {
                    await Task.Delay(_random.Next(2000, 4000));
                    var (ok, msg) = await _core.BumpLotPostAsync(node, gameId, csrf ?? "");
                    
                    _totalAttempts++;
                    if (ok)
                    {
                        raised++;
                        _successfulBumps++;
                        AppendPluginLog($"[SUCCESS] Лот {node}: {msg}");
                    }
                    else
                    {
                        AppendPluginLog($"[FP] Лот {node}: {msg}");
                    }
                }
                else
                {
                    AppendPluginLog($"[SKIP] Лот {node}: Не найдена кнопка поднятия.");
                }

                UpdateStats();
                int delay = _random.Next(4000, 8000);
                await Task.Delay(delay);
            }

            if (raised > 0) AppendPluginLog($"[RESULT] Поднято лотов: {raised}");
            
            if (_isAutoBumpRunning)
            {
                // Безопасный парсинг настроек
                int hours = 4, jitter = 15;
                if (int.TryParse(IntervalInput.Text, out int h)) hours = h;
                if (int.TryParse(JitterInput.Text, out int j)) jitter = j;
                if (hours < 1) hours = 1;

                var nextDelay = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(_random.Next(0, jitter));
                
                _autoBumpTimer.Interval = nextDelay;
                _autoBumpTimer.Start();
                
                var nextTime = DateTime.Now + nextDelay;
                StatusText.Text = $"След: {nextTime:HH:mm}";
                AppendPluginLog($"[SLEEP] Следующий запуск: {nextTime:HH:mm:ss}");
            }
            else
            {
                StatusText.Text = "ГОТОВ";
            }
        }

        private void UpdateStats()
        {
            TxtTotal.Text = _totalAttempts.ToString();
            TxtSuccess.Text = _successfulBumps.ToString();
        }

        private void AppendPluginLog(string msg)
        {
            if (_sharedLog == null) return;
            
            Brush color = Brushes.White;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[WARN]") || msg.Contains("[FP]")) color = Brushes.Orange;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.LightGreen;
            else if (msg.Contains("[WAIT]")) color = Brushes.Gray;
            
            _sharedLog.Add(new FPBooster.MainWindow.LogEntry 
            { 
                Text = $"{DateTime.Now:HH:mm:ss} {msg}", 
                Color = color 
            });
            
            try {
                if (PluginLog.Items.Count > 0)
                    PluginLog.ScrollIntoView(PluginLog.Items[PluginLog.Items.Count - 1]);
            } catch {}
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e) => _sharedLog?.Clear();
    }
}