using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects; // Для эффектов, если нужно в коде
using System.Windows.Threading;
using FPBooster.Plugins;

// --- ПСЕВДОНИМЫ ---
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Thickness = System.Windows.Thickness;

namespace FPBooster.Plugins
{
    public partial class AdvProfileStatView : UserControl, IPlugin
    {
        private readonly AdvProfileStatCore _core;
        private string _goldenKey = "";
        
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private ObservableCollection<FPBooster.MainWindow.LogEntry> _localLog;
        
        private DispatcherTimer _statusTimer;
        private Dictionary<string, object> _currentStats = new();
        private bool _isInitialized = false;
        private bool _isDemoData = true;

        public string Id => "adv_profile_stat";
        public string DisplayName => "Статистика профиля";
        public UserControl GetView() => this;

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
            _localLog = new ObservableCollection<FPBooster.MainWindow.LogEntry>();
            
            if (PluginLogListBox != null)
                PluginLogListBox.ItemsSource = _localLog;
            
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _statusTimer.Tick += async (s, e) => await RefreshStats();
            _statusTimer.Start();
        }

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
            
            if (!string.IsNullOrEmpty(_goldenKey))
            {
                var client = FPBooster.FunPay.ProfileParser.CreateClient(_goldenKey);
                _core.SetHttpClient(client);
                _isDemoData = false;
                _core.SetUseRealData(true);
            }
            
            if (!_isInitialized)
            {
                _isInitialized = true;
                LoadData();
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
        }

        public void SetTheme(string themeKey) { }

        private async void LoadData()
        {
            if (_isDemoData) AppendLog("[INFO] Демо-режим (нет ключа)", Brushes.Orange);
            await RefreshStats();
        }

        private async void QuickRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (QuickRefreshButton == null) return;
            try
            {
                QuickRefreshButton.IsEnabled = false;
                AppendLog("[INFO] Обновление баланса...");
                SetStatus("Обновление...", Brushes.Yellow);

                var balances = await _core.FetchQuickWithdrawAsync();
                
                if (!_currentStats.ContainsKey("canWithdraw")) 
                    _currentStats["canWithdraw"] = new Dictionary<string, string>();
                
                var dict = _currentStats["canWithdraw"] as Dictionary<string, string>;
                if (dict != null)
                {
                    foreach(var kvp in balances) dict[kvp.Key] = kvp.Value;
                }
                
                UpdateStatsDisplay(_currentStats);
                AppendLog("[SUCCESS] Баланс обновлен", Brushes.LightGreen);
                SetStatus("OK", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Ошибка: {ex.Message}", Brushes.IndianRed);
                SetStatus("Ошибка", Brushes.IndianRed);
            }
            finally
            {
                QuickRefreshButton.IsEnabled = true;
            }
        }

        private async Task RefreshStats()
        {
            try
            {
                if (QuickRefreshButton != null) QuickRefreshButton.IsEnabled = false;
                SetStatus("Загрузка...", Brushes.Yellow);
                
                var stats = await _core.GetStatsAsync();
                _currentStats = stats;
                UpdateStatsDisplay(stats);
                
                if (LastUpdateText != null)
                    LastUpdateText.Text = $"Обновлено: {DateTime.Now:HH:mm:ss}";
                
                SetStatus("OK", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] {ex.Message}", Brushes.IndianRed);
                SetStatus("Ошибка", Brushes.IndianRed);
            }
            finally
            {
                if (QuickRefreshButton != null) QuickRefreshButton.IsEnabled = true;
            }
        }

        private void UpdateStatsDisplay(Dictionary<string, object> stats)
        {
            if (stats.ContainsKey("canWithdraw") && stats["canWithdraw"] is Dictionary<string, string> balance)
            {
                if (NowValue != null) NowValue.Text = balance.GetValueOrDefault("now", "0 ₽");
                if (RubValue != null) RubValue.Text = balance.GetValueOrDefault("RUB", "0 ₽");
                if (UsdValue != null) UsdValue.Text = balance.GetValueOrDefault("USD", "0 $");
                if (EurValue != null) EurValue.Text = balance.GetValueOrDefault("EUR", "0 €");
            }

            if (StatsContainer == null) return;
            StatsContainer.Children.Clear();

            var sales = stats["sales"] as Dictionary<string, object>;
            var refunds = stats["refunds"] as Dictionary<string, object>;
            var salesPrice = stats["salesPrice"] as Dictionary<string, object>;
            var refundsPrice = stats["refundsPrice"] as Dictionary<string, object>;

            foreach (var p in _periods)
            {
                var card = CreateStatCard(p.Label, p.Key, sales, refunds, salesPrice, refundsPrice);
                StatsContainer.Children.Add(card);
            }
        }

        private Border CreateStatCard(string title, string key, 
            Dictionary<string, object> sales, Dictionary<string, object> refunds,
            Dictionary<string, object> salesPrice, Dictionary<string, object> refundsPrice)
        {
            var countSale = sales != null && sales.ContainsKey(key) ? sales[key].ToString() : "0";
            var countRefund = refunds != null && refunds.ContainsKey(key) ? refunds[key].ToString() : "0";

            var card = new Border
            {
                Style = (Style)FindResource("PluginCard"),
                Margin = new Thickness(0, 0, 15, 15),
                Width = 230, // Чуть шире для красоты
                MinHeight = 130
            };

            var stack = new StackPanel();
            
            // Заголовок карточки с легким неоном
            var headerBlock = new TextBlock 
            { 
                Text = title, 
                FontWeight = FontWeights.Bold, 
                FontSize = 16,
                Foreground = (Brush)FindResource("BrushText"),
                Margin = new Thickness(0,0,0,15)
            };
            // Неон для заголовков карточек (слабый)
            headerBlock.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.2 };
            
            stack.Children.Add(headerBlock);

            stack.Children.Add(CreateRow("Продажи:", countSale, Brushes.LightGreen, salesPrice, key));
            stack.Children.Add(new Border { Height = 8 });
            stack.Children.Add(CreateRow("Возвраты:", countRefund, Brushes.IndianRed, refundsPrice, key));

            card.Child = stack;
            return card;
        }

        private UIElement CreateRow(string label, string count, Brush color, Dictionary<string, object> prices, string key)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            
            panel.Children.Add(new TextBlock 
            { 
                Text = label, 
                Width = 70, 
                Foreground = (Brush)FindResource("BrushSubText"),
                VerticalAlignment = VerticalAlignment.Center
            });
            
            var countText = new TextBlock 
            { 
                Text = count, 
                FontWeight = FontWeights.Bold, 
                Foreground = color,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Неон для цифр
            countText.Effect = new DropShadowEffect { Color = ((SolidColorBrush)color).Color, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.4 };
            
            DockPanel.SetDock(countText, Dock.Left);
            panel.Children.Add(countText);

            string priceStr = "";
            if (prices != null)
            {
                foreach(var k in prices.Keys)
                {
                    if (k.StartsWith(key + "_"))
                    {
                        string curr = k.Split('_')[1];
                        string val = prices[k].ToString();
                        priceStr += $"{val:F0}{curr} ";
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(priceStr))
            {
                var priceBlock = new TextBlock 
                { 
                    Text = priceStr, 
                    FontSize = 11, 
                    Foreground = (Brush)FindResource("BrushSubText"), 
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(priceBlock, Dock.Right);
                panel.Children.Add(priceBlock);
            }

            return panel;
        }

        private void AppendLog(string msg, Brush? color = null)
        {
            var c = color ?? Brushes.White;
            var time = DateTime.Now.ToString("HH:mm:ss");
            var entry = new FPBooster.MainWindow.LogEntry { Text = $"[{time}] {msg}", Color = c };

            Dispatcher.Invoke(() => 
            {
                _localLog.Insert(0, entry);
                if (_localLog.Count > 100) _localLog.RemoveAt(_localLog.Count - 1);

                if (_sharedLog != null)
                {
                    _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = $"[STAT] {msg}", Color = c });
                }
            });
        }
        
        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
             _localLog.Clear();
        }

        private void SetStatus(string text, Brush color)
        {
            if (PluginStatus != null)
            {
                PluginStatus.Text = text;
                PluginStatus.Foreground = color;
            }
        }
    }
}