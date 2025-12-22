using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FPBooster.FunPay;
using FPBooster.Config;

// === РЕШЕНИЕ КОНФЛИКТОВ ИМЕН (CS0104) ===
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Application = System.Windows.Application;
// ========================================

namespace FPBooster.Plugins
{
    public partial class AdvProfileStatView : UserControl, IPlugin, INotifyPropertyChanged
    {
        // --- РЕАЛИЗАЦИЯ IPLUGIN (CS0535) ---
        public string Id => "adv_profile_stat";
        public string DisplayName => "Статистика профиля";

        public UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            // Сохраняем ключ, переданный ядром
            _goldenKey = goldenKey;
            
            // Автозапуск загрузки при инициализации
            if (!string.IsNullOrEmpty(_goldenKey))
            {
                Dispatcher.BeginInvoke(new Action(async () => await LoadStatsAsync()), DispatcherPriority.Background);
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> log)
        {
            _sharedLog = log;
        }

        public void SetTheme(string theme)
        {
            // Метод заглушка, если смена темы обрабатывается глобально через ресурсы
        }
        // ------------------------------------

        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private string _goldenKey = "";
        
        // Свойства для привязки (Binding) в XAML
        private string _salesText = "Загрузка...";
        public string SalesText { get => _salesText; set { _salesText = value; OnPropertyChanged(); } }

        private string _refundsText = "Загрузка...";
        public string RefundsText { get => _refundsText; set { _refundsText = value; OnPropertyChanged(); } }

        private string _balanceText = "---";
        public string BalanceText { get => _balanceText; set { _balanceText = value; OnPropertyChanged(); } }

        private string _activeOrdersText = "0";
        public string ActiveOrdersText { get => _activeOrdersText; set { _activeOrdersText = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AdvProfileStatView()
        {
            InitializeComponent();
            DataContext = this; 
        }

        // Кнопка обновления из UI
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadStatsAsync();
        }

        private async void QuickRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadStatsAsync();
        }

        private async Task LoadStatsAsync()
        {
            try
            {
                // Если ключ не пришел через InitNodes, пробуем взять из конфига
                if (string.IsNullOrEmpty(_goldenKey))
                {
                    try { _goldenKey = ConfigManager.Load().GoldenKey; } catch { }
                }

                if (string.IsNullOrEmpty(_goldenKey))
                {
                    AppendLog("[ERR] Golden Key не найден! Укажите его в главном окне.");
                    SalesText = "Нет ключа";
                    return;
                }

                AppendLog("Загрузка статистики профиля...");

                // Используем ProfileParser для создания клиента (он уже настроен с заголовками)
                using var client = ProfileParser.CreateClient(_goldenKey);

                // Загружаем заказы (5 страниц для точности)
                var (orders, balance) = await Stats.FetchRecentOrdersAsync(client, maxPages: 5);
                
                if (orders == null || orders.Count == 0)
                {
                    AppendLog("[WARN] Заказы не найдены или ошибка доступа.");
                    // Не сбрасываем в 0, вдруг просто сеть лагнула, оставляем старое или пишем прочерк
                    if (BalanceText == "---") BalanceText = "Ошибка";
                    return;
                }

                // Считаем статистику
                var ((sales, refunds), (salesPrice, refundsPrice)) = Stats.BucketByPeriod(orders);

                // Обновляем UI
                SalesText = $"{sales["all"]} шт. ({salesPrice.GetValueOrDefault("all_₽"):N0} ₽)";
                RefundsText = $"{refunds["all"]} шт. ({refundsPrice.GetValueOrDefault("all_₽"):N0} ₽)";
                
                if (balance.ContainsKey("now"))
                    BalanceText = balance["now"];
                
                // Активные заказы
                var activeCount = orders.Count(o => o.Status.ToLower().Contains("оплачен") || o.Status.ToLower().Contains("wait") || o.Status.ToLower().Contains("ожидает"));
                ActiveOrdersText = activeCount.ToString();

                AppendLog($"[SUCCESS] Обновлено. Заказов: {orders.Count}, Баланс: {BalanceText}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Ошибка статистики: {ex.Message}");
                SalesText = "Ошибка";
            }
        }

        private void AppendLog(string msg)
        {
            if (_sharedLog == null) return;
            
            Brush color = Brushes.White;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[WARN]")) color = Brushes.Orange;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.LightGreen;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _sharedLog.Insert(0, new FPBooster.MainWindow.LogEntry 
                { 
                    Text = $"[{DateTime.Now:HH:mm:ss}] [Stats] {msg}", 
                    Color = color 
                });
            });
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
            // Очистка локального лога не требуется, так как пишем в глобальный, 
            // но если нужно, можно добавить очистку _sharedLog (с осторожностью)
        }
    }
}