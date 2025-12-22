using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FPBooster.FunPay;
using FPBooster.Config;

// Псевдонимы (УСТРАНЕНИЕ КОНФЛИКТОВ)
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using WpfApplication = System.Windows.Application;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment; 
using VerticalAlignment = System.Windows.VerticalAlignment;
using FontWeight = System.Windows.FontWeight;
using FontWeights = System.Windows.FontWeights;
using Grid = System.Windows.Controls.Grid;
using Thickness = System.Windows.Thickness;
using FontFamily = System.Windows.Media.FontFamily;

namespace FPBooster.Plugins
{
    public partial class AdvProfileStatView : UserControl, IPlugin
    {
        public string Id => "adv_profile_stat";
        public string DisplayName => "Статистика профиля";
        public UserControl GetView() => this;

        private readonly AdvProfileStatCore _core;
        private string _goldenKey = "";
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private DispatcherTimer _statusTimer;
        private Dictionary<string, object> _currentStats = new();
        private bool _isInitialized = false;
        private bool _isDemoData = true;
        private bool _isLoading = false;

        public AdvProfileStatView()
        {
            InitializeComponent();
            _core = new AdvProfileStatCore(new System.Net.Http.HttpClient());
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _statusTimer.Tick += async (s, e) => await RefreshStatsAsync();
        }

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
            if (string.IsNullOrEmpty(_goldenKey)) _goldenKey = TryFindGoldenKey();

            if (!string.IsNullOrEmpty(_goldenKey))
            {
                var client = ProfileParser.CreateClient(_goldenKey);
                _core.SetHttpClient(client);
                _core.SetUseRealData(true);
                _isDemoData = false;
            }
            else
            {
                _core.SetUseRealData(false);
                _isDemoData = true;
            }

            if (!_isInitialized)
            {
                _isInitialized = true;
                _statusTimer.Start();
                Dispatcher.BeginInvoke(new Action(async () => await RefreshStatsAsync()), DispatcherPriority.Background);
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> log) => _sharedLog = log;
        public void SetTheme(string theme) { }

        // --- КНОПКИ ---
        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshStatsAsync();
        
        private async void QuickRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (QuickRefreshButton == null) return;
            try
            {
                QuickRefreshButton.IsEnabled = false;
                var balances = await _core.FetchQuickWithdrawAsync();
                
                if (!_currentStats.ContainsKey("canWithdraw")) _currentStats["canWithdraw"] = new Dictionary<string, string>();
                var dict = _currentStats["canWithdraw"] as Dictionary<string, string>;
                if (dict != null)
                {
                    foreach(var kvp in balances) dict[kvp.Key] = kvp.Value;
                }
                
                UpdateStatsDisplay(_currentStats);
                AppendLog("[SUCCESS] Баланс обновлен");
            }
            catch (Exception ex) { AppendLog($"[ERR] {ex.Message}"); }
            finally { QuickRefreshButton.IsEnabled = true; }
        }
        
        private void OnClearPluginLog(object sender, RoutedEventArgs e) { } 

        private string TryFindGoldenKey()
        {
            if (!string.IsNullOrEmpty(_goldenKey)) return _goldenKey;
            try { return ConfigManager.Load().GoldenKey; } catch { }
            try {
                var mw = WpfApplication.Current.MainWindow;
                if (mw != null) {
                    var input = mw.FindName("GoldenKeyInput") as TextBox;
                    if (input != null) return input.Text.Trim();
                }
            } catch { }
            return "";
        }

        private async Task RefreshStatsAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            if (PluginStatus != null) PluginStatus.Text = "Загрузка...";

            try
            {
                if (_isDemoData && string.IsNullOrEmpty(_goldenKey))
                {
                    _goldenKey = TryFindGoldenKey();
                    if (!string.IsNullOrEmpty(_goldenKey))
                    {
                        var client = ProfileParser.CreateClient(_goldenKey);
                        _core.SetHttpClient(client);
                        _core.SetUseRealData(true);
                        _isDemoData = false;
                    }
                }

                AppendLog(_isDemoData ? "[INFO] Демо-режим..." : "[INFO] Загрузка с FunPay...");
                var stats = await _core.FetchStatsAsync();

                if (stats != null)
                {
                    _currentStats = stats;
                    UpdateStatsDisplay(stats);
                    if (LastUpdateText != null) LastUpdateText.Text = $"Обновлено: {DateTime.Now:HH:mm:ss}";
                    if (PluginStatus != null) PluginStatus.Text = "OK";
                    
                    // Показываем в логе, сколько заказов просканировано
                    int count = stats.ContainsKey("totalOrdersParsed") ? (int)stats["totalOrdersParsed"] : 0;
                    AppendLog($"[SUCCESS] Обработано заказов: {count}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] {ex.Message}");
                if (PluginStatus != null) PluginStatus.Text = "Error";
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void UpdateStatsDisplay(Dictionary<string, object> data)
        {
            if (StatsGrid == null) return;
            StatsGrid.Children.Clear();
            StatsGrid.RowDefinitions.Clear();

            // Баланс
            if (data.ContainsKey("canWithdraw") && data["canWithdraw"] is Dictionary<string, string> balance)
            {
                if (NowValue != null) NowValue.Text = balance.GetValueOrDefault("now", "0 ₽");
                if (RubValue != null) RubValue.Text = balance.GetValueOrDefault("RUB", "0 ₽");
                if (UsdValue != null) UsdValue.Text = balance.GetValueOrDefault("USD", "0 $");
                if (EurValue != null) EurValue.Text = balance.GetValueOrDefault("EUR", "0 €");
            }

            // Таблица
            var sales = data["sales"] as Dictionary<string, object>;
            var salesPrice = data["salesPrice"] as Dictionary<string, object>;
            var refunds = data["refunds"] as Dictionary<string, object>;
            var refundsPrice = data["refundsPrice"] as Dictionary<string, object>;

            string[] periods = { "day", "week", "month", "all" };
            string[] periodNames = { "Сегодня", "Неделя", "Месяц", "Все время" };

            for (int i = 0; i < periods.Length; i++)
            {
                var key = periods[i];
                StatsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

                AddCell(i, 0, periodNames[i], MediaBrushes.Gray, FontWeights.Normal);
                
                var sCount = sales != null && sales.ContainsKey(key) ? sales[key].ToString() : "0";
                AddCell(i, 1, sCount, MediaBrushes.LightGreen, FontWeights.Bold, WpfHorizontalAlignment.Center);

                var sPriceStr = FormatPrice(salesPrice, key);
                AddCell(i, 2, sPriceStr, MediaBrushes.White, FontWeights.Normal, WpfHorizontalAlignment.Right);

                var rCount = refunds != null && refunds.ContainsKey(key) ? refunds[key].ToString() : "0";
                var colRef = rCount == "0" ? MediaBrushes.Gray : MediaBrushes.IndianRed;
                AddCell(i, 3, rCount, colRef, FontWeights.Bold, WpfHorizontalAlignment.Center);

                var rPriceStr = FormatPrice(refundsPrice, key);
                AddCell(i, 4, rPriceStr, colRef, FontWeights.Normal, WpfHorizontalAlignment.Right);
            }
        }

        private string FormatPrice(Dictionary<string, object>? prices, string period)
        {
            if (prices == null) return "0 ₽";
            List<string> parts = new List<string>();
            
            decimal rub = prices.ContainsKey($"{period}_₽") ? (decimal)prices[$"{period}_₽"] : 0;
            decimal usd = prices.ContainsKey($"{period}_$") ? (decimal)prices[$"{period}_$"] : 0;
            decimal eur = prices.ContainsKey($"{period}_€") ? (decimal)prices[$"{period}_€"] : 0;

            if (rub > 0) parts.Add($"{rub:N0} ₽");
            if (usd > 0) parts.Add($"{usd:N2} $");
            if (eur > 0) parts.Add($"{eur:N2} €");

            if (parts.Count == 0) return "0 ₽";
            return string.Join(" + ", parts);
        }

        private void AddCell(int row, int col, string text, MediaBrush color, FontWeight weight, WpfHorizontalAlignment align = WpfHorizontalAlignment.Left)
        {
            var txt = new TextBlock
            {
                Text = text, Foreground = color, FontWeight = weight, FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align,
                Margin = new Thickness(0, 4, 0, 4)
            };
            if (col == 2 || col == 4) txt.FontFamily = new FontFamily("Consolas");
            
            Grid.SetRow(txt, row);
            Grid.SetColumn(txt, col);
            StatsGrid.Children.Add(txt);
        }

        private void AppendLog(string msg)
        {
            if (_sharedLog == null) return;
            MediaBrush color = MediaBrushes.White;
            if (msg.Contains("[ERR]")) color = MediaBrushes.IndianRed;
            else if (msg.Contains("[INFO]")) color = MediaBrushes.LightSkyBlue;
            else if (msg.Contains("[SUCCESS]")) color = MediaBrushes.LightGreen;

            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                _sharedLog.Insert(0, new FPBooster.MainWindow.LogEntry 
                { 
                    Text = $"[{DateTime.Now:HH:mm:ss}] [Stats] {msg}", 
                    Color = color 
                });
            });
        }
    }
}