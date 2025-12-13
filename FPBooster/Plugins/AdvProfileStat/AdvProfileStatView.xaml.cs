using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FPBooster.Plugins;

using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace FPBooster.Plugins
{
    public partial class AdvProfileStatView : UserControl, IPlugin
    {
        private readonly AdvProfileStatCore _core;
        private string _goldenKey = "";
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private DispatcherTimer _statusTimer;
        private Dictionary<string, object> _currentStats = new();
        private bool _isInitialized = false;
        private bool _isDemoData = true;
        private bool _isLoading = false;
        private int _logCount = 0;
        
        public string Id => "adv_profile_stat";
        public string DisplayName => "Статистика профиля";
        
        private readonly List<(string Key, string Label)> _periods = new()
        {
            ("day", "День 🌞"),
            ("week", "Неделя 📅"), 
            ("month", "Месяц 🗓️"),
            ("all", "Все время ♾️")
        };

        public AdvProfileStatView()
        {
            InitializeComponent();
            _core = new AdvProfileStatCore(new HttpClient());
            
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => UpdateStatus();
            _statusTimer.Start();

            this.Loaded += (s, e) => 
            {
                _isInitialized = true;
                UpdateStatus("Готов к работе");
                AnimateEntrance();
                UpdateLogCounter();
            };
        }

        private void AnimateEntrance()
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        public UserControl GetView() => this;

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog ?? throw new ArgumentNullException(nameof(sharedLog));
            
            if (PluginLog != null) PluginLog.ItemsSource = _sharedLog;

            _sharedLog.CollectionChanged += (s, e) =>
            {
                _logCount = _sharedLog.Count;
                UpdateLogCounter();
                try {
                    if (PluginLog != null && PluginLog.Items.Count > 0)
                        PluginLog.ScrollIntoView(PluginLog.Items[PluginLog.Items.Count - 1]);
                } catch { }
            };
            UpdateLogCounter();
        }

        private void UpdateLogCounter()
        {
            if (LogCounter != null) LogCounter.Text = _logCount.ToString();
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
            if (_sharedLog != null)
            {
                _sharedLog.Clear();
                _logCount = 0;
                UpdateLogCounter();
            }
        }

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey ?? "";
            
            try
            {
                var client = AutoBumpCore.CreateClientWithCookie(_goldenKey);
                _core.SetHttpClient(client);
                
                AppendLog($"[PLUGIN] GoldenKey: {(string.IsNullOrEmpty(_goldenKey) ? "не установлен" : "установлен")}");
                
                if (!string.IsNullOrEmpty(_goldenKey))
                {
                    _core.SetUseRealData(true);
                    AppendLog("[INFO] Режим реальных данных активирован");
                }
                else
                {
                    _core.SetUseRealData(false);
                    AppendLog("[WARN] GoldenKey пуст, используются демо-данные");
                }
                
                UpdateStatus("Готов к работе");
                if (!string.IsNullOrEmpty(_goldenKey) && !_isLoading)
                {
                    _ = LoadStatsAsync();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Ошибка инициализации: {ex.Message}");
                UpdateStatus("Ошибка подключения");
            }
        }

        public void SetTheme(string themeKey) { }

        private void UpdateStatus(string? status = null)
        {
            if (!_isInitialized) return;
            if (status != null)
            {
                if (PluginStatus != null) 
                {
                    PluginStatus.Text = status;
                    PluginStatus.Foreground = Brushes.LightGreen;
                }
                return;
            }

            if (string.IsNullOrEmpty(_goldenKey))
            {
                if (PluginStatus != null)
                {
                    PluginStatus.Text = "Требуется Golden Key";
                    PluginStatus.Foreground = Brushes.Orange;
                }
            }
            else
            {
                if (PluginStatus != null)
                {
                    var statusText = "Готов";
                    if (_isDemoData)
                    {
                        statusText += " (демо)";
                        PluginStatus.Foreground = Brushes.Gold;
                    }
                    else
                    {
                        PluginStatus.Foreground = Brushes.LightGreen;
                    }
                    PluginStatus.Text = statusText;
                }
            }
        }

        private void AppendLog(string message)
        {
            if (_sharedLog == null) return;
            
            Brush color = Brushes.White;
            if (Application.Current.Resources["BrushText"] is SolidColorBrush themeBrush)
                color = themeBrush;

            if (message.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (message.Contains("[WARN]")) color = Brushes.Orange;
            else if (message.Contains("[INFO]")) color = Brushes.LightSkyBlue;
            else if (message.Contains("[SUCCESS]")) color = Brushes.LightGreen;
            else if (message.Contains("[DEBUG]")) color = Brushes.Gray;

            _sharedLog.Add(new FPBooster.MainWindow.LogEntry
            {
                Text = $"{DateTime.Now:HH:mm:ss} {message}",
                Color = color
            });
        }

        private string Elide(string text, int maxChars = 40)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxChars ? text : text.Substring(0, maxChars - 1).TrimEnd() + "…";
        }

        private (string Display, string Full) FormatPriceDict(Dictionary<string, decimal> priceDict, string periodKey)
        {
            var parts = new List<string>();
            if (priceDict != null)
            {
                foreach (var kvp in priceDict)
                {
                    if (kvp.Key.StartsWith(periodKey + "_") || kvp.Key == $"all_{periodKey}")
                    {
                        var currency = kvp.Key.Contains('_') ? kvp.Key.Split('_')[1] : "¤";
                        parts.Add($"{kvp.Value:F2} {currency}");
                    }
                }
            }
            
            if (parts.Count == 0)
                return ("0 ¤", "0 ¤");
            var joined = string.Join("; ", parts);
            return (Elide(joined, 40), joined);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadStatsAsync(true);
        }

        private async System.Threading.Tasks.Task LoadStatsAsync(bool isRefresh = false)
        {
            if (_isLoading) return;
            if (string.IsNullOrEmpty(_goldenKey))
            {
                AppendLog("[WARN] Golden Key не установлен");
                UpdateStatus("Требуется Golden Key");
                return;
            }

            _isLoading = true;
            UpdateStatus("Загрузка...");
            if (RefreshButton != null)
            {
                RefreshButton.IsEnabled = false;
                RefreshButton.Content = "⏳";
            }

            try
            {
                AppendLog("[DEBUG] Запрос статистики...");
                var stats = await _core.GenerateAdvProfileAsync();
                
                if (stats.TryGetValue("dataSource", out var dsObj))
                {
                    var dataSource = dsObj?.ToString();
                    _isDemoData = dataSource == "demo";
                    if(_isDemoData) AppendLog("[INFO] Демо данные");
                    else AppendLog("[SUCCESS] Данные с FunPay получены");
                }
                else
                {
                    _isDemoData = true;
                }

                _currentStats = stats;
                UpdateStatsDisplay(stats);
                UpdateStatus(_isDemoData ? "Демо-данные" : "Обновлено");
                
                if (LastUpdateText != null)
                    LastUpdateText.Text = $"Последнее обновление: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Ошибка загрузки: {ex.Message}");
                UpdateStatus("Ошибка");
            }
            finally
            {
                _isLoading = false;
                if (RefreshButton != null)
                {
                    RefreshButton.IsEnabled = true;
                    RefreshButton.Content = "🔄 Полное обновление";
                }
            }
        }

        private void UpdateStatsDisplay(Dictionary<string, object> stats)
        {
            if (!_isInitialized) return;
            var titleText = "📊 Статистика профиля";
            if (_isDemoData) titleText += " (демо)";

            if (PluginTitle != null) PluginTitle.Text = titleText;
            if (StatsGrid != null)
            {
                StatsGrid.Children.Clear();
                StatsGrid.RowDefinitions.Clear();

                // Header
                StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var headers = new[] { "Период", "Продаж", "Сумма", "Возврат", "Сумма" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var header = CreateHeaderLabel(headers[i]);
                    Grid.SetRow(header, 0);
                    Grid.SetColumn(header, i);
                    StatsGrid.Children.Add(header);
                }

                var sales = stats.TryGetValue("sales", out var s) && s is Dictionary<string, object> sd ? sd : new Dictionary<string, object>();
                var refunds = stats.TryGetValue("refunds", out var r) && r is Dictionary<string, object> rd ? rd : new Dictionary<string, object>();
                var salesPrice = stats.TryGetValue("salesPrice", out var sp) && sp is Dictionary<string, object> spd ? spd : new Dictionary<string, object>();
                var refundsPrice = stats.TryGetValue("refundsPrice", out var rp) && rp is Dictionary<string, object> rpd ? rpd : new Dictionary<string, object>();

                for (int row = 0; row < _periods.Count; row++)
                {
                    var period = _periods[row];
                    StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    // Background stripe for readability
                    var rowBorder = new Border 
                    { 
                        Background = (row % 2 == 0) ? new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)) : Brushes.Transparent,
                        CornerRadius = new CornerRadius(4)
                    };
                    Grid.SetRow(rowBorder, row + 1);
                    Grid.SetColumnSpan(rowBorder, 5);
                    StatsGrid.Children.Add(rowBorder);

                    // 1. Period
                    var periodLabel = CreateDataLabel(period.Label, true, Brushes.White); // Will default to white, can adjust
                    if (Application.Current.Resources["BrushAccentLight"] is SolidColorBrush acc) periodLabel.Foreground = acc;
                    periodLabel.HorizontalAlignment = HorizontalAlignment.Left;
                    Grid.SetRow(periodLabel, row + 1);
                    Grid.SetColumn(periodLabel, 0);
                    StatsGrid.Children.Add(periodLabel);

                    // 2. Count Sales
                    var salesCount = sales.TryGetValue(period.Key, out var sc) ? sc.ToString() : "0";
                    var salesLabel = CreateDataLabel(salesCount ?? "0", false, Brushes.LightGreen);
                    Grid.SetRow(salesLabel, row + 1);
                    Grid.SetColumn(salesLabel, 1);
                    StatsGrid.Children.Add(salesLabel);

                    // 3. Sum Sales
                    var (salesDisplay, salesFull) = FormatPriceDict(ConvertPriceDict(salesPrice), period.Key);
                    var salesPriceLabel = CreateMonoLabel(salesDisplay, salesFull, Brushes.LightGreen);
                    Grid.SetRow(salesPriceLabel, row + 1);
                    Grid.SetColumn(salesPriceLabel, 2);
                    StatsGrid.Children.Add(salesPriceLabel);

                    // 4. Count Refund
                    var refundsCount = refunds.TryGetValue(period.Key, out var rc) ? rc.ToString() : "0";
                    var refundsLabel = CreateDataLabel(refundsCount ?? "0", false, Brushes.IndianRed);
                    Grid.SetRow(refundsLabel, row + 1);
                    Grid.SetColumn(refundsLabel, 3);
                    StatsGrid.Children.Add(refundsLabel);

                    // 5. Sum Refund
                    var (refundsDisplay, refundsFull) = FormatPriceDict(ConvertPriceDict(refundsPrice), period.Key);
                    var refundsPriceLabel = CreateMonoLabel(refundsDisplay, refundsFull, Brushes.IndianRed);
                    Grid.SetRow(refundsPriceLabel, row + 1);
                    Grid.SetColumn(refundsPriceLabel, 4);
                    StatsGrid.Children.Add(refundsPriceLabel);
                }
            }

            if (stats.TryGetValue("canWithdraw", out var canWithdrawObj))
            {
                 if (canWithdrawObj is Dictionary<string, string> canWithdrawStr)
                {
                    if (NowValue != null) NowValue.Text = canWithdrawStr.TryGetValue("now", out var now) ? now : "0 ¤";
                    if (EurValue != null) EurValue.Text = canWithdrawStr.TryGetValue("EUR", out var eur) ? eur : "0 €";
                    if (RubValue != null) RubValue.Text = canWithdrawStr.TryGetValue("RUB", out var rub) ? rub : "0 ₽";
                    if (UsdValue != null) UsdValue.Text = canWithdrawStr.TryGetValue("USD", out var usd) ? usd : "0 $";
                }
            }
        }

        private Dictionary<string, decimal> ConvertPriceDict(Dictionary<string, object> priceDict)
        {
            var result = new Dictionary<string, decimal>();
            if (priceDict == null) return result;

            foreach (var kvp in priceDict)
            {
                if (decimal.TryParse(kvp.Value?.ToString(), out decimal value))
                    result[kvp.Key] = value;
            }
            return result;
        }

        private TextBlock CreateHeaderLabel(string text)
        {
            var brush = Brushes.Gray;
            if (Application.Current.Resources["BrushSubText"] is SolidColorBrush scb) brush = scb;

            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(4,0,4,8)
            };
        }

        private TextBlock CreateDataLabel(string text, bool bold, Brush color)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 13, 
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(4,8,4,8)
            };
        }

        private TextBlock CreateMonoLabel(string text, string? tooltip, Brush color)
        {
            var label = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(8,8,4,8)
            };
            if (!string.IsNullOrEmpty(tooltip))
                ToolTipService.SetToolTip(label, tooltip);
            return label;
        }

        private async void QuickRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (QuickRefreshButton == null) return;
            try
            {
                QuickRefreshButton.IsEnabled = false;
                QuickRefreshButton.Content = "⏳";

                AppendLog("[DEBUG] Обновление баланса...");
                var canWithdraw = await _core.FetchQuickWithdrawAsync();
                if (_currentStats.ContainsKey("canWithdraw"))
                    _currentStats["canWithdraw"] = canWithdraw;
                else
                    _currentStats.Add("canWithdraw", canWithdraw);
                
                UpdateStatsDisplay(_currentStats);
                AppendLog("[SUCCESS] Баланс обновлён");
                
                if (LastUpdateText != null)
                    LastUpdateText.Text = $"Последнее обновление: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Ошибка баланса: {ex.Message}");
            }
            finally
            {
                QuickRefreshButton.IsEnabled = true;
                QuickRefreshButton.Content = "🔄 Обновить баланс";
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            UpdateStatus();
        }
    }
}