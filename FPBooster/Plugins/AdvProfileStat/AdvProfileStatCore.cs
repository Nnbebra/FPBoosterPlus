using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FPBooster.FunPay;

namespace FPBooster.Plugins
{
    public class AdvProfileStatCore
    {
        private HttpClient _client;
        private bool _useRealData = false;
        
        // Примерные курсы (для верхней цифры общего баланса)
        private const decimal UsdRate = 93m;
        private const decimal EurRate = 101m;

        public AdvProfileStatCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;
        public void SetUseRealData(bool use) => _useRealData = use;

        public async Task<Dictionary<string, object>> FetchStatsAsync()
        {
            if (!_useRealData) return GetDemoStats();

            // 1. Грузим
            var orders = await Stats.FetchOrdersAsync(_client);
            var balances = await Stats.FetchBalancesAsync(_client);

            // 2. Расчет общего баланса (парсим строки "100 ₽" в числа и складываем)
            decimal totalRub = ParseMoney(balances["RUB"]) + 
                               (ParseMoney(balances["USD"]) * UsdRate) + 
                               (ParseMoney(balances["EUR"]) * EurRate);
            
            var balanceData = new Dictionary<string, string>
            {
                ["now"] = $"{totalRub:N0} ₽",
                ["RUB"] = balances["RUB"],
                ["USD"] = balances["USD"],
                ["EUR"] = balances["EUR"]
            };

            // 3. Статистика
            var sales = new Dictionary<string, int> { ["all"]=0, ["day"]=0, ["week"]=0, ["month"]=0 };
            var refunds = new Dictionary<string, int> { ["all"]=0, ["day"]=0, ["week"]=0, ["month"]=0 };
            var salesPrice = new Dictionary<string, decimal>();
            var refundsPrice = new Dictionary<string, decimal>();

            void AddSum(Dictionary<string, decimal> d, string p, string c, decimal v) 
            { 
                string k = $"{p}_{c}"; 
                if(!d.ContainsKey(k)) d[k]=0; 
                d[k]+=v; 
            }

            foreach (var o in orders)
            {
                // Пропускаем незавершенные (если не успех и не возврат)
                if (!o.IsSuccess && !o.IsRefund) continue;

                // Периоды
                bool isDay = o.TimeAgo.TotalHours < 24;
                bool isWeek = o.TimeAgo.TotalDays < 7;
                bool isMonth = o.TimeAgo.TotalDays < 30;

                if (o.IsSuccess)
                {
                    sales["all"]++; AddSum(salesPrice, "all", o.Currency, o.Price);
                    if(isDay) { sales["day"]++; AddSum(salesPrice, "day", o.Currency, o.Price); }
                    if(isWeek) { sales["week"]++; AddSum(salesPrice, "week", o.Currency, o.Price); }
                    if(isMonth) { sales["month"]++; AddSum(salesPrice, "month", o.Currency, o.Price); }
                }
                else if (o.IsRefund)
                {
                    refunds["all"]++; AddSum(refundsPrice, "all", o.Currency, o.Price);
                    if(isDay) { refunds["day"]++; AddSum(refundsPrice, "day", o.Currency, o.Price); }
                    if(isWeek) { refunds["week"]++; AddSum(refundsPrice, "week", o.Currency, o.Price); }
                    if(isMonth) { refunds["month"]++; AddSum(refundsPrice, "month", o.Currency, o.Price); }
                }
            }

            // Добавляем список заказов для дебага (опционально)
            return new Dictionary<string, object>
            {
                ["canWithdraw"] = balanceData,
                ["sales"] = sales,
                ["salesPrice"] = salesPrice,
                ["refunds"] = refunds,
                ["refundsPrice"] = refundsPrice,
                ["totalOrdersParsed"] = orders.Count
            };
        }

        public async Task<Dictionary<string, string>> FetchQuickWithdrawAsync()
        {
            var b = await Stats.FetchBalancesAsync(_client);
            decimal total = ParseMoney(b["RUB"]) + (ParseMoney(b["USD"]) * UsdRate) + (ParseMoney(b["EUR"]) * EurRate);
            return new Dictionary<string, string>
            {
                ["now"] = $"{total:N0} ₽",
                ["RUB"] = b["RUB"],
                ["USD"] = b["USD"],
                ["EUR"] = b["EUR"]
            };
        }

        private decimal ParseMoney(string raw)
        {
            var clean = Regex.Replace(raw, @"[^\d.,]", "").Replace(" ", "");
            if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(clean.Replace(".", ","), System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("ru-RU"), out d)) return d;
            return 0;
        }

        private Dictionary<string, object> GetDemoStats()
        {
            var sp = new Dictionary<string, decimal> { ["all_₽"] = 0 };
            var rp = new Dictionary<string, decimal> { ["all_₽"] = 0 };
            return new Dictionary<string, object>
            {
                ["canWithdraw"] = new Dictionary<string, string> { ["now"]="ДЕМО", ["RUB"]="0", ["USD"]="0", ["EUR"]="0" },
                ["sales"] = new Dictionary<string, int> { ["all"]=0, ["day"]=0, ["week"]=0, ["month"]=0 },
                ["salesPrice"] = sp,
                ["refunds"] = new Dictionary<string, int> { ["all"]=0, ["day"]=0, ["week"]=0, ["month"]=0 },
                ["refundsPrice"] = rp,
                ["totalOrdersParsed"] = 0
            };
        }
    }
}