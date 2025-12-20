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
using System.Reflection;

// Явные типы
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace FPBooster.Plugins
{
    public partial class AutoBumpView : System.Windows.Controls.UserControl, IPlugin
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
        }

        public System.Windows.Controls.UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
            _core.SetHttpClient(AutoBumpCore.CreateClientWithCookie(_goldenKey));
            
            _nodes.Clear();
            foreach (var n in nodes) _nodes.Add(n);
            
            if (!_isInitialized && _nodes.Count > 0)
            {
                AppendPluginLog($"[INFO] Загружено разделов: {_nodes.Count}");
                _isInitialized = true;
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> logCollection)
        {
            _sharedLog = logCollection;
            PluginLog.ItemsSource = _sharedLog; 
        }

        public void SetTheme(string themeKey) { }

        private void OnEcoModeFromPlugin(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
            {
                var method = mainWindow.GetType().GetMethod("OnEcoModeClick", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (method != null) method.Invoke(mainWindow, new object[] { sender, e });
                else MessageBox.Show("Функция недоступна.", "Инфо");
            }
        }

        private async void OnStartAutoClick(object sender, RoutedEventArgs e)
        {
            if (_isAutoBumpRunning) return;
            if (_nodes.Count == 0) { MessageBox.Show("Нет разделов (NodeID)!"); return; }

            _isAutoBumpRunning = true;
            BtnStart.Visibility = Visibility.Collapsed;
            BtnStop.Visibility = Visibility.Visible;
            StatusText.Text = "ЗАПУСК...";
            StatusText.Foreground = Brushes.LightGreen;
            StartPulseAnimation(); 

            await RunAutoBumpCycle();
        }

        private void OnStopAutoClick(object sender, RoutedEventArgs e)
        {
            _isAutoBumpRunning = false;
            _autoBumpTimer.Stop();
            BtnStart.Visibility = Visibility.Visible;
            BtnStop.Visibility = Visibility.Collapsed;
            StatusText.Text = "ОСТАНОВЛЕНО";
            StatusText.Foreground = Brushes.Orange;
            StopPulseAnimation(); 
            AppendPluginLog("[INFO] Авто-поднятие остановлено");
        }

        private async void OnQuickBumpClick(object sender, RoutedEventArgs e)
        {
            if (_isQuickBumpRunning) return;
            _isQuickBumpRunning = true;
            var btn = sender as System.Windows.Controls.Button;
            if(btn!=null) btn.IsEnabled = false;

            AppendPluginLog("[START] Быстрое поднятие (1 круг)...");
            await RunAutoBumpCycle();
            
            AppendPluginLog("[END] Быстрое поднятие завершено");
            _isQuickBumpRunning = false;
            if(btn!=null) btn.IsEnabled = true;
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Очистить общий журнал?", "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                _sharedLog?.Clear();
        }

        // --- УМНЫЙ ЦИКЛ ПОДНЯТИЯ ---
        private async Task RunAutoBumpCycle()
        {
            _autoBumpTimer.Stop(); 

            AppendPluginLog($"[CYCLE] Проход по {_nodes.Count} разделам...");
            int raised = 0;
            TimeSpan maxRequiredWait = TimeSpan.Zero; // Для хранения максимального времени ожидания

            foreach (var node in _nodes)
            {
                if (!_isAutoBumpRunning && !_isQuickBumpRunning) break;

                // 1. Получаем Инфо + Время ожидания, если есть ошибка
                var (csrf, gameId, error, waitTime) = await _core.GetLotInfoAsync(node);

                if (!string.IsNullOrEmpty(error))
                {
                    if (error.Contains("Таймер"))
                    {
                        AppendPluginLog($"[WAIT] Раздел {node}: {error}");
                        // Если FunPay говорит "ждите", запоминаем это время
                        if (waitTime.HasValue && waitTime.Value > maxRequiredWait)
                            maxRequiredWait = waitTime.Value;
                    }
                    else 
                    {
                        AppendPluginLog($"[ERR] Раздел {node}: {error}");
                    }
                }
                else if (!string.IsNullOrEmpty(gameId))
                {
                    // 2. Поднимаем
                    await Task.Delay(_random.Next(1500, 3000));
                    var (ok, msg, postWaitTime) = await _core.BumpLotPostAsync(node, gameId, csrf ?? "");
                    _totalAttempts++;

                    if (ok)
                    {
                        raised++;
                        _successfulBumps++;
                        AppendPluginLog($"[SUCCESS] Раздел {node}: {msg}");
                    }
                    else
                    {
                        AppendPluginLog($"[FAIL] Раздел {node}: {msg}");
                        // Если ошибка пришла из POST запроса
                        if (postWaitTime.HasValue && postWaitTime.Value > maxRequiredWait)
                            maxRequiredWait = postWaitTime.Value;
                    }
                }
                else
                {
                    AppendPluginLog($"[ERR] Раздел {node}: Кнопка поднятия не найдена.");
                }

                UpdateStats();
                await Task.Delay(_random.Next(3000, 6000));
            }

            if (raised > 0) AppendPluginLog($"[RESULT] Успешно поднято: {raised}");

            // --- УМНЫЙ РАСЧЕТ ВРЕМЕНИ СЛЕДУЮЩЕГО ЗАПУСКА ---
            if (_isAutoBumpRunning)
            {
                int h = 4, j = 15;
                if (int.TryParse(IntervalInput.Text, out int hh)) h = hh;
                if (int.TryParse(JitterInput.Text, out int jj)) j = jj;

                TimeSpan nextDelay;

                // Если FunPay попросил подождать (есть maxRequiredWait > 0)
                if (maxRequiredWait > TimeSpan.Zero)
                {
                    // Ставим время, которое просит FunPay + защитный буфер 2-5 минут + рандомный разброс
                    // Разброс тут только в плюс, чтобы не попытаться раньше времени
                    var buffer = TimeSpan.FromMinutes(_random.Next(2, 5));
                    var jitter = TimeSpan.FromMinutes(_random.Next(0, j)); 
                    nextDelay = maxRequiredWait + buffer + jitter;
                    
                    AppendPluginLog($"[SMART] FunPay просит подождать. Пауза: {nextDelay:hh\\:mm\\:ss}");
                }
                else
                {
                    // Если всё ок, используем стандартный интервал пользователя
                    // Разброс теперь работает в +/- (половина разброса в минус, половина в плюс)
                    // Например, если Jitter=20 мин, то разброс от -10 до +10 мин
                    int halfJitter = j / 2;
                    int randomMinutes = _random.Next(-halfJitter, halfJitter);
                    
                    nextDelay = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(randomMinutes);
                    
                    // Защита от отрицательного времени
                    if (nextDelay < TimeSpan.FromMinutes(10)) nextDelay = TimeSpan.FromMinutes(10);
                }

                _autoBumpTimer.Interval = nextDelay;
                _autoBumpTimer.Start();
                StatusText.Text = $"СЛЕДУЮЩИЙ: {DateTime.Now + nextDelay:HH:mm}";
            }
            else if (!_isQuickBumpRunning)
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
            if (msg.Contains("[ERR]") || msg.Contains("[FAIL]")) color = Brushes.IndianRed;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.LightGreen;
            else if (msg.Contains("[CYCLE]")) color = Brushes.Cyan;
            else if (msg.Contains("[START]")) color = Brushes.Gold;
            else if (msg.Contains("[SMART]")) color = Brushes.Violet; // Новый цвет для умного таймера
            else if (msg.Contains("[WAIT]")) color = Brushes.Orange;
            
            Dispatcher.Invoke(() => 
            {
                _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = $"{DateTime.Now:HH:mm:ss} {msg}", Color = color });
                try { 
                    if (VisualTreeHelper.GetChild(PluginLog, 0) is Decorator border && border.Child is ScrollViewer sv) 
                        sv.ScrollToBottom(); 
                } catch { }
            });
        }

        private void StartPulseAnimation()
        {
            var anim = new DoubleAnimation
            {
                From = 0.2, To = 1.0, Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
            };
            StatusGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, anim);
        }

        private void StopPulseAnimation()
        {
            StatusGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
            StatusGlow.Opacity = 0;
        }
    }
}