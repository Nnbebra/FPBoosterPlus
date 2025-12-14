using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FPBooster.FunPay;

// --- ПСЕВДОНИМЫ ДЛЯ УСТРАНЕНИЯ КОНФЛИКТОВ ---
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
// ----------------------------------------------

namespace FPBooster.Plugins
{
    public partial class AdvProfileStatView : UserControl, IPlugin
    {
        public string Id => "adv_profile_stat";
        public string DisplayName => "Статистика профиля";

        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private string _goldenKey = "";
        
        // Ссылки на элементы UI (для безопасного доступа)
        private TextBlock? _txtTotalSales;
        private TextBlock? _txtTotalRefunds;
        private TextBlock? _txtBalance;
        
        public AdvProfileStatView()
        {
            InitializeComponent();
        }

        public UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
        }

        public void SetTheme(string themeKey) { }

        // --- ОБРАБОТЧИКИ СОБЫТИЙ (FIX CS1061) ---

        private void QuickRefreshButton_Click(object sender, RoutedEventArgs e) => LoadStats();
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadStats();
        
        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
             // Если у вас есть локальный лог в XAML (например PluginLog), очищаем его
             // Либо очищаем общий, если нужно
             // _sharedLog?.Clear(); // Обычно общий лог чистят из главного окна
        }

        // Старый метод, если он был привязан
        private void OnLoadClick(object sender, RoutedEventArgs e) => LoadStats();

        // --- ЛОГИКА ---

        private async void LoadStats()
        {
            if (string.IsNullOrEmpty(_goldenKey))
            {
                MessageBox.Show("Введите Golden Key в главном окне!");
                return;
            }

            // Безопасный поиск элементов, если они не доступны напрямую
            _txtTotalSales = this.FindName("TxtTotalSales") as TextBlock;
            _txtTotalRefunds = this.FindName("TxtTotalRefunds") as TextBlock;
            _txtBalance = this.FindName("TxtBalance") as TextBlock;

            try
            {
                AppendLog("[INFO] Загрузка статистики...");
                
                var client = new System.Net.Http.HttpClient();
                var handler = new System.Net.Http.HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
                handler.CookieContainer.Add(new System.Net.Cookie("golden_key", _goldenKey, "/", "funpay.com"));
                client = new System.Net.Http.HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                // Получаем данные
                var (orders, balance) = await Stats.FetchRecentOrdersAsync(client);
                var ((sales, refunds), (salesPrice, refundsPrice)) = Stats.BucketByPeriod(orders);

                // Обновляем UI
                if (_txtTotalSales != null) 
                    _txtTotalSales.Text = $"{sales["all"]} шт. ({salesPrice.GetValueOrDefault("all_₽"):F0} ₽)";
                
                if (_txtTotalRefunds != null) 
                    _txtTotalRefunds.Text = $"{refunds["all"]} шт. ({refundsPrice.GetValueOrDefault("all_₽"):F0} ₽)";
                
                if (_txtBalance != null) 
                    _txtBalance.Text = balance.GetValueOrDefault("now", "0 ₽");

                AppendLog($"[SUCCESS] Данные обновлены. Заказов загружено: {orders.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Ошибка: {ex.Message}");
            }
        }

        private void AppendLog(string msg)
        {
            if (_sharedLog == null) return;
            
            Brush color = Brushes.White;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.LightGreen;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _sharedLog.Insert(0, new FPBooster.MainWindow.LogEntry 
                { 
                    Text = $"[{DateTime.Now:HH:mm:ss}] {msg}", 
                    Color = color 
                });
            });
        }
    }
}