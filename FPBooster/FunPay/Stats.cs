using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FPBooster.FunPay
{
    public static class Stats
    {
        /// <summary>
        /// Получает заказы со страницы продаж и баланс со страницы финансов.
        /// </summary>
        public static async Task<(List<OrderItem> orders, Dictionary<string, string> canWithdraw)> FetchRecentOrdersAsync(HttpClient session, int maxPages = 1)
        {
            var orders = new List<OrderItem>();
            var canWithdraw = new Dictionary<string, string>();
            
            // Инициализация безопасных значений
            EnsureCanWithdrawKeys(canWithdraw);

            try
            {
                // --- ШАГ 1: Получаем заказы (https://funpay.com/orders/trade) ---
                var ordersUrl = "https://funpay.com/orders/trade";
                var ordersResponse = await session.GetAsync(ordersUrl);
                
                if (ordersResponse.IsSuccessStatusCode)
                {
                    var html = await ordersResponse.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Строки таблицы - это теги <a> с классом tc-item
                    var rows = doc.DocumentNode.SelectNodes("//a[contains(@class, 'tc-item')]");
                    
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            try 
                            {
                                var item = ParseOrderRow(row);
                                if (item != null) orders.Add(item);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[WARN] Ошибка парсинга строки заказа: {ex.Message}");
                            }
                        }
                    }
                }

                // --- ШАГ 2: Получаем баланс (https://funpay.com/account/balance) ---
                var balanceUrl = "https://funpay.com/account/balance";
                var balanceResponse = await session.GetAsync(balanceUrl);

                if (balanceResponse.IsSuccessStatusCode)
                {
                    var html = await balanceResponse.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    
                    ParseBalanceFromPage(doc, canWithdraw);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] FetchRecentOrdersAsync failed: {ex.Message}");
            }

            return (orders, canWithdraw);
        }

        private static void ParseBalanceFromPage(HtmlDocument doc, Dictionary<string, string> canWithdraw)
        {
            try
            {
                // Баланс лежит в <span class="balances-value">
                var balanceNodes = doc.DocumentNode.SelectNodes("//span[contains(@class, 'balances-value')]");
                
                if (balanceNodes != null)
                {
                    foreach (var node in balanceNodes)
                    {
                        var text = node.InnerText.Trim(); // Пример: "0.26 €"
                        
                        if (text.Contains("₽")) canWithdraw["RUB"] = text;
                        else if (text.Contains("$")) canWithdraw["USD"] = text;
                        else if (text.Contains("€")) canWithdraw["EUR"] = text;
                    }
                }
                
                // Логика выбора основного баланса для отображения "Сейчас"
                if (canWithdraw.ContainsKey("RUB") && canWithdraw["RUB"] != "0 ₽") canWithdraw["now"] = canWithdraw["RUB"];
                else if (canWithdraw.ContainsKey("USD") && canWithdraw["USD"] != "0 $") canWithdraw["now"] = canWithdraw["USD"];
                else if (canWithdraw.ContainsKey("EUR") && canWithdraw["EUR"] != "0 €") canWithdraw["now"] = canWithdraw["EUR"];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Ошибка парсинга баланса: {ex.Message}");
            }
        }

        private static OrderItem? ParseOrderRow(HtmlNode row)
        {
            var idNode = row.SelectSingleNode(".//div[contains(@class, 'tc-order')]");
            var statusNode = row.SelectSingleNode(".//div[contains(@class, 'tc-status')]");
            var priceNode = row.SelectSingleNode(".//div[contains(@class, 'tc-price')]");
            var dateNode = row.SelectSingleNode(".//div[contains(@class, 'tc-date-time')]");

            if (idNode == null || priceNode == null) return null;

            var orderId = idNode.InnerText.Trim().Replace("#", "");
            var status = statusNode?.InnerText.Trim() ?? "Unknown";
            var date = dateNode?.InnerText.Trim() ?? "";

            // Извлекаем текст цены
            var priceRaw = priceNode.InnerText.Trim();
            priceRaw = System.Net.WebUtility.HtmlDecode(priceRaw);

            var currency = "¤";
            if (priceRaw.Contains("₽") || priceRaw.Contains("RUB")) currency = "₽";
            else if (priceRaw.Contains("$") || priceRaw.Contains("USD")) currency = "$";
            else if (priceRaw.Contains("€") || priceRaw.Contains("EUR")) currency = "€";

            // Оставляем только цифры, точки и запятые
            var cleanPrice = Regex.Replace(priceRaw, @"[^\d.,]", "").Replace(",", ".");
            if (cleanPrice.EndsWith(".")) cleanPrice = cleanPrice.TrimEnd('.');

            decimal price = 0;
            decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

            return new OrderItem
            {
                OrderId = orderId,
                Status = status,
                Price = price,
                Currency = currency,
                Date = date
            };
        }

        private static void EnsureCanWithdrawKeys(Dictionary<string, string> canWithdraw)
        {
            if (!canWithdraw.ContainsKey("now")) canWithdraw["now"] = "0 ¤";
            if (!canWithdraw.ContainsKey("EUR")) canWithdraw["EUR"] = "0 €";
            if (!canWithdraw.ContainsKey("RUB")) canWithdraw["RUB"] = "0 ₽";
            if (!canWithdraw.ContainsKey("USD")) canWithdraw["USD"] = "0 $";
        }

        public static ((Dictionary<string, int> sales, Dictionary<string, int> refunds), 
                      (Dictionary<string, decimal> salesPrice, Dictionary<string, decimal> refundsPrice)) 
                      BucketByPeriod(List<OrderItem> orders)
        {
            var sales = new Dictionary<string, int> { ["day"] = 0, ["week"] = 0, ["month"] = 0, ["all"] = 0 };
            var refunds = new Dictionary<string, int> { ["day"] = 0, ["week"] = 0, ["month"] = 0, ["all"] = 0 };
            var salesPrice = new Dictionary<string, decimal>();
            var refundsPrice = new Dictionary<string, decimal>();

            foreach (var order in orders)
            {
                var s = order.Status.ToLower();
                bool isRefund = s.Contains("возврат") || s.Contains("refund") || s.Contains("отменен");
                bool isCompleted = s.Contains("закрыт") || s.Contains("completed") || s.Contains("подтвержден") || s.Contains("оплачен");

                if (!isRefund && !isCompleted) continue; 

                if (isRefund)
                {
                    refunds["all"]++;
                    AddToPrice(refundsPrice, "all", order.Currency, order.Price);
                }
                else
                {
                    sales["all"]++;
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

    public class OrderItem
    {
        public string OrderId { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Price { get; set; }
        public string Currency { get; set; } = "";
        public string Date { get; set; } = "";
    }
}