using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FPBooster.FunPay; // Используем глобальный Stats и OrderItem

namespace FPBooster.Plugins
{
    public class AdvProfileStatCore
    {
        private HttpClient _client;
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FPBooster");
        private static readonly string StoreFile = Path.Combine(AppDataDir, "adv_profile_stat.json");

        private bool _useRealData = false;
        
        // --- ЛОГИКА БАЛАНСА (ИЗ BACKUP) ---
        private const decimal UsdRate = 93m;
        private const decimal EurRate = 101m;
        // ----------------------------------

        public AdvProfileStatCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;
        public void SetUseRealData(bool useRealData) => _useRealData = useRealData;

        // --- Сохранение и загрузка настроек (без изменений) ---
        private Dictionary<string, object> LoadEvents()
        {
            try
            {
                if (!File.Exists(StoreFile)) return new Dictionary<string, object>();
                var json = File.ReadAllText(StoreFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object>();
                return JsonElementToDictionary(doc.RootElement);
            }
            catch { return new Dictionary<string, object>(); }
        }

        private Dictionary<string, object> JsonElementToDictionary(JsonElement element)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    dict[prop.Name] = JsonElementToDictionary(prop.Value);
                else
                    dict[prop.Name] = prop.Value.GetString();
            }
            return dict;
        }
        // ------------------------------------------------------

        public async Task<Dictionary<string, string>> FetchQuickWithdrawAsync()
        {
            if (!_useRealData) 
                return new Dictionary<string, string> { ["now"]="12,450 ₽", ["RUB"]="12,450 ₽", ["USD"]="0 $", ["EUR"]="0 €" };

            // Логика расчета баланса перенесена из backup
            var b = await FPBooster.FunPay.Stats.FetchBalancesAsync(_client);
            decimal total = ParseMoney(b["RUB"]) + (ParseMoney(b["USD"]) * UsdRate) + (ParseMoney(b["EUR"]) * EurRate);

            return new Dictionary<string, string>
            {
                ["now"] = $"{total:N0} ₽",
                ["RUB"] = b["RUB"],
                ["USD"] = b["USD"],
                ["EUR"] = b["EUR"]
            };
        }

        public async Task<Dictionary<string, object>> GetStatsAsync()
        {
            if (!_useRealData) return GetDemoStats();

            // 1. Получаем данные
            var orders = await FPBooster.FunPay.Stats.FetchOrdersAsync(_client);
            var rawBalances = await FPBooster.FunPay.Stats.FetchBalancesAsync(_client);

            // 2. Расчет ОБЩЕГО БАЛАНСА (ИЗ BACKUP)
            decimal totalRub = ParseMoney(rawBalances["RUB"]) + 
                               (ParseMoney(rawBalances["USD"]) * UsdRate) + 
                               (ParseMoney(rawBalances["EUR"]) * EurRate);

            var balanceData = new Dictionary<string, string>
            {
                ["now"] = $"{totalRub:N0} ₽",
                ["RUB"] = rawBalances["RUB"],
                ["USD"] = rawBalances["USD"],
                ["EUR"] = rawBalances["EUR"]
            };

            // 3. Старая логика статистики продаж (BucketByPeriod)
            var ((sales, refunds), (salesPrice, refundsPrice)) = BucketByPeriod(orders);

            return new Dictionary<string, object>
            {
                ["sales"] = new Dictionary<string, object>
                {
                    ["day"] = sales["day"], ["week"] = sales["week"], ["month"] = sales["month"], ["all"] = sales["all"]
                },
                ["salesPrice"] = salesPrice.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                ["refunds"] = new Dictionary<string, object>
                {
                    ["day"] = refunds["day"], ["week"] = refunds["week"], ["month"] = refunds["month"], ["all"] = refunds["all"]
                },
                ["refundsPrice"] = refundsPrice.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                ["canWithdraw"] = balanceData // Передаем рассчитанный баланс
            };
        }

        // --- Хелпер для парсинга денег (ИЗ BACKUP) ---
        private decimal ParseMoney(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return 0;
            var clean = Regex.Replace(raw, @"[^\d.,]", "").Replace(" ", "");
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(clean.Replace(".", ","), NumberStyles.Any, new CultureInfo("ru-RU"), out d)) return d;
            return 0;
        }

        private Dictionary<string, object> GetDemoStats()
        {
            var demoOrders = new List<OrderItem>
            {
                new OrderItem { OrderId = "12345", IsSuccess = true, Price = 150.00m, Currency = "₽", TimeAgo = TimeSpan.FromHours(2) },
                new OrderItem { OrderId = "12346", IsSuccess = true, Price = 500.00m, Currency = "₽", TimeAgo = TimeSpan.FromDays(2) },
                new OrderItem { OrderId = "12347", IsRefund = true, Price = 150.00m, Currency = "₽", TimeAgo = TimeSpan.FromDays(5) },
                new OrderItem { OrderId = "12348", IsSuccess = true, Price = 25.00m, Currency = "$", TimeAgo = TimeSpan.FromDays(20) },
                new OrderItem { OrderId = "12349", IsSuccess = true, Price = 20.00m, Currency = "€", TimeAgo = TimeSpan.FromDays(40) }
            };
            
            var demoCanWithdraw = new Dictionary<string, string>
            {
                ["now"] = "12,450 ₽",
                ["EUR"] = "112.05 €", 
                ["RUB"] = "12,450 ₽",
                ["USD"] = "137.50 $"
            };

            var ((sales, refunds), (salesPrice, refundsPrice)) = BucketByPeriod(demoOrders);
            
            return new Dictionary<string, object>
            {
                ["sales"] = new Dictionary<string, object> 
                { 
                    ["day"] = sales["day"], ["week"] = sales["week"], ["month"] = sales["month"], ["all"] = sales["all"] 
                },
                ["salesPrice"] = salesPrice.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                ["refunds"] = new Dictionary<string, object> 
                { 
                    ["day"] = refunds["day"], ["week"] = refunds["week"], ["month"] = refunds["month"], ["all"] = refunds["all"] 
                },
                ["refundsPrice"] = refundsPrice.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                ["canWithdraw"] = demoCanWithdraw
            };
        }

        // --- СТАРАЯ ЛОГИКА ПРОДАЖ (НЕ ТРОГАЕМ) ---
        private static ((Dictionary<string, int> sales, Dictionary<string, int> refunds), (Dictionary<string, decimal> salesPrice, Dictionary<string, decimal> refundsPrice)) BucketByPeriod(List<OrderItem> orders)
        {
            var sales = new Dictionary<string, int> { ["day"] = 0, ["week"] = 0, ["month"] = 0, ["all"] = 0 };
            var refunds = new Dictionary<string, int> { ["day"] = 0, ["week"] = 0, ["month"] = 0, ["all"] = 0 };

            var salesPrice = new Dictionary<string, decimal>();
            var refundsPrice = new Dictionary<string, decimal>();

            foreach (var order in orders)
            {
                bool isRefund = order.IsRefund;
                bool isCompleted = order.IsSuccess;

                if (!isRefund && !isCompleted) continue; 

                var t = order.TimeAgo;
                bool isDay = t.TotalHours < 24;
                bool isWeek = t.TotalDays < 7;
                bool isMonth = t.TotalDays < 30;

                if (isRefund)
                {
                    if (isDay) refunds["day"]++;
                    if (isWeek) refunds["week"]++;
                    if (isMonth) refunds["month"]++;
                    refunds["all"]++;

                    if (isDay) AddToPrice(refundsPrice, "day", order.Currency, order.Price);
                    if (isWeek) AddToPrice(refundsPrice, "week", order.Currency, order.Price);
                    if (isMonth) AddToPrice(refundsPrice, "month", order.Currency, order.Price);
                    AddToPrice(refundsPrice, "all", order.Currency, order.Price);
                }
                else
                {
                    if (isDay) sales["day"]++;
                    if (isWeek) sales["week"]++;
                    if (isMonth) sales["month"]++;
                    sales["all"]++;

                    if (isDay) AddToPrice(salesPrice, "day", order.Currency, order.Price);
                    if (isWeek) AddToPrice(salesPrice, "week", order.Currency, order.Price);
                    if (isMonth) AddToPrice(salesPrice, "month", order.Currency, order.Price);
                    AddToPrice(salesPrice, "all", order.Currency, order.Price);
                }
            }

            return ((sales, refunds), (salesPrice, refundsPrice));
        }

        private static void AddToPrice(Dictionary<string, decimal> dict, string period, string currency, decimal amount)
        {
            var key = $"{period}_{currency}";
            if (!dict.ContainsKey(key)) dict[key] = 0;
            dict[key] += amount;
        }
    }
}