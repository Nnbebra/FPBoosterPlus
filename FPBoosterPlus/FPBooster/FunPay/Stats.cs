using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

// Псевдоним для устранения конфликта с WinForms HtmlDocument
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FPBooster.FunPay
{
    public static class Stats
    {
        public static async Task<(List<OrderItem> orders, Dictionary<string, string> canWithdraw)> FetchRecentOrdersAsync(HttpClient session, int maxPages = 1)
        {
            var orders = new List<OrderItem>();
            var canWithdraw = new Dictionary<string, string>();
            
            EnsureCanWithdrawKeys(canWithdraw);

            try
            {
                // 1. Заказы
                var ordersUrl = "https://funpay.com/orders/trade";
                var ordersResponse = await session.GetAsync(ordersUrl);
                
                if (ordersResponse.IsSuccessStatusCode)
                {
                    var html = await ordersResponse.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var rows = doc.DocumentNode.SelectNodes("//a[contains(@class, 'tc-item')]");
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            try {
                                var item = ParseOrderRow(row);
                                if (item != null) orders.Add(item);
                            } catch { }
                        }
                    }
                }

                // 2. Баланс
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
                Console.WriteLine($"[ERROR] Stats: {ex.Message}");
            }

            return (orders, canWithdraw);
        }

        private static void ParseBalanceFromPage(HtmlDocument doc, Dictionary<string, string> canWithdraw)
        {
            try
            {
                var balanceNodes = doc.DocumentNode.SelectNodes("//span[contains(@class, 'balances-value')]");
                if (balanceNodes != null)
                {
                    foreach (var node in balanceNodes)
                    {
                        var text = node.InnerText.Trim();
                        if (text.Contains("₽")) canWithdraw["RUB"] = text;
                        else if (text.Contains("$")) canWithdraw["USD"] = text;
                        else if (text.Contains("€")) canWithdraw["EUR"] = text;
                    }
                }
                
                if (canWithdraw.ContainsKey("RUB") && canWithdraw["RUB"] != "0 ₽") canWithdraw["now"] = canWithdraw["RUB"];
                else if (canWithdraw.ContainsKey("USD") && canWithdraw["USD"] != "0 $") canWithdraw["now"] = canWithdraw["USD"];
                else if (canWithdraw.ContainsKey("EUR") && canWithdraw["EUR"] != "0 €") canWithdraw["now"] = canWithdraw["EUR"];
            }
            catch { }
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

            var priceRaw = System.Net.WebUtility.HtmlDecode(priceNode.InnerText.Trim());
            var currency = "¤";
            if (priceRaw.Contains("₽") || priceRaw.Contains("RUB")) currency = "₽";
            else if (priceRaw.Contains("$") || priceRaw.Contains("USD")) currency = "$";
            else if (priceRaw.Contains("€") || priceRaw.Contains("EUR")) currency = "€";

            var cleanPrice = Regex.Replace(priceRaw, @"[^\d.,]", "").Replace(",", ".");
            if (cleanPrice.EndsWith(".")) cleanPrice = cleanPrice.TrimEnd('.');

            decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price);

            return new OrderItem { OrderId = orderId, Status = status, Price = price, Currency = currency, Date = date };
        }

        private static void EnsureCanWithdrawKeys(Dictionary<string, string> canWithdraw)
        {
            if (!canWithdraw.ContainsKey("now")) canWithdraw["now"] = "0 ¤";
            if (!canWithdraw.ContainsKey("EUR")) canWithdraw["EUR"] = "0 €";
            if (!canWithdraw.ContainsKey("RUB")) canWithdraw["RUB"] = "0 ₽";
            if (!canWithdraw.ContainsKey("USD")) canWithdraw["USD"] = "0 $";
        }

        public static ((Dictionary<string, int>, Dictionary<string, int>), (Dictionary<string, decimal>, Dictionary<string, decimal>)) BucketByPeriod(List<OrderItem> orders)
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

                if (isRefund) {
                    refunds["all"]++;
                    AddToPrice(refundsPrice, "all", order.Currency, order.Price);
                } else {
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