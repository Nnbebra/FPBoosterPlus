using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

// Псевдоним
using AgilityHtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FPBooster.FunPay
{
    public static class Stats
    {
        public static async Task<List<OrderItem>> FetchOrdersAsync(HttpClient client)
        {
            var orders = new List<OrderItem>();
            try
            {
                var html = await client.GetStringAsync("https://funpay.com/orders/trade");
                var doc = new AgilityHtmlDocument();
                doc.LoadHtml(html);

                // Ищем строки заказов
                var items = doc.DocumentNode.SelectNodes("//a[contains(@class, 'tc-item')]");
                if (items == null) return orders;

                foreach (var item in items)
                {
                    try
                    {
                        var order = new OrderItem();
                        
                        // --- 1. ID ЗАКАЗА ---
                        var idNode = item.SelectSingleNode(".//div[contains(@class, 'tc-order')]");
                        order.OrderId = idNode?.InnerText?.Trim().Replace("#", "") ?? "???";

                        // --- 2. СТАТУС (ПО CSS КЛАССУ) ---
                        // <div class="tc-status text-success">Закрыт</div>
                        var statusNode = item.SelectSingleNode(".//div[contains(@class, 'tc-status')]");
                        var statusClass = statusNode?.GetAttributeValue("class", "") ?? "";
                        var statusText = statusNode?.InnerText?.Trim() ?? "";

                        // Если есть класс text-success -> это продажа
                        // Если есть класс text-warning или текст "Возврат" -> это возврат
                        order.IsSuccess = statusClass.Contains("text-success") || statusText == "Закрыт" || statusText == "Оплачен";
                        order.IsRefund = statusClass.Contains("text-warning") || statusText == "Возврат" || statusText == "Отменен";

                        // --- 3. ЦЕНА ---
                        // <div class="tc-price ...">0.07 <span class="unit">€</span></div>
                        var priceNode = item.SelectSingleNode(".//div[contains(@class, 'tc-price')]");
                        if (priceNode != null)
                        {
                            var rawHtml = priceNode.InnerHtml; // Берем HTML, чтобы найти span class="unit"
                            var rawText = WebUtility.HtmlDecode(priceNode.InnerText).Trim(); // "0.07 €"

                            // Определяем валюту
                            if (rawText.Contains("€") || rawText.Contains("EUR")) order.Currency = "€";
                            else if (rawText.Contains("$") || rawText.Contains("USD")) order.Currency = "$";
                            else order.Currency = "₽";

                            // Чистим цену: оставляем цифры, точку, запятую. Убираем пробелы (разделители тысяч).
                            var cleanPrice = Regex.Replace(rawText, @"[^\d.,]", "").Replace(" ", "");
                            
                            // Парсим (FunPay может давать "100.00" или "100,00")
                            if (!decimal.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                            {
                                decimal.TryParse(cleanPrice.Replace(".", ","), NumberStyles.Any, new CultureInfo("ru-RU"), out p);
                            }
                            order.Price = p;
                        }

                        // --- 4. ДАТА (ВРЕМЯ НАЗАД) ---
                        // <div class="tc-date-left">9 месяцев назад</div>
                        var dateLeftNode = item.SelectSingleNode(".//div[contains(@class, 'tc-date-left')]");
                        if (dateLeftNode != null)
                        {
                            order.TimeAgo = ParseRussianTimeAgo(dateLeftNode.InnerText.Trim());
                        }
                        else
                        {
                            order.TimeAgo = TimeSpan.FromDays(999); // Старый заказ
                        }

                        orders.Add(order);
                    }
                    catch { }
                }
            }
            catch { }
            return orders;
        }

        public static async Task<Dictionary<string, string>> FetchBalancesAsync(HttpClient client)
        {
            var balances = new Dictionary<string, string> { ["RUB"] = "0 ₽", ["USD"] = "0 $", ["EUR"] = "0 €" };
            try
            {
                var html = await client.GetStringAsync("https://funpay.com/account/balance");
                var doc = new AgilityHtmlDocument();
                doc.LoadHtml(html);

                var nodes = doc.DocumentNode.SelectNodes("//span[contains(@class, 'balances-value')]");
                if (nodes != null)
                {
                    foreach (var n in nodes)
                    {
                        var t = WebUtility.HtmlDecode(n.InnerText).Trim();
                        if (t.Contains("₽")) balances["RUB"] = t;
                        else if (t.Contains("$")) balances["USD"] = t;
                        else if (t.Contains("€")) balances["EUR"] = t;
                    }
                }
            }
            catch { }
            return balances;
        }

        private static TimeSpan ParseRussianTimeAgo(string text)
        {
            // text: "9 месяцев назад", "5 минут назад"
            try
            {
                text = text.ToLower();
                if (text.Contains("только") || text.Contains("сейчас")) return TimeSpan.Zero;
                if (text.Contains("вчера")) return TimeSpan.FromDays(1);

                var match = Regex.Match(text, @"(\d+)\s+([а-яa-z]+)");
                if (match.Success)
                {
                    int val = int.Parse(match.Groups[1].Value);
                    string unit = match.Groups[2].Value; 

                    if (unit.StartsWith("сек") || unit.StartsWith("sec")) return TimeSpan.FromSeconds(val);
                    if (unit.StartsWith("мин") || unit.StartsWith("min")) return TimeSpan.FromMinutes(val);
                    if (unit.StartsWith("час") || unit.StartsWith("hour")) return TimeSpan.FromHours(val);
                    if (unit.StartsWith("д") || unit.StartsWith("day") || unit.StartsWith("сутки")) return TimeSpan.FromDays(val);
                    if (unit.StartsWith("нед") || unit.StartsWith("week")) return TimeSpan.FromDays(val * 7);
                    if (unit.StartsWith("мес") || unit.StartsWith("mon")) return TimeSpan.FromDays(val * 30);
                    if (unit.StartsWith("год") || unit.StartsWith("лет") || unit.StartsWith("year")) return TimeSpan.FromDays(val * 365);
                }
            }
            catch { }
            return TimeSpan.FromDays(365);
        }
    }

    public class OrderItem
    {
        public string OrderId { get; set; } = "";
        public bool IsSuccess { get; set; } // Успешная продажа
        public bool IsRefund { get; set; }  // Возврат
        public decimal Price { get; set; }
        public string Currency { get; set; } = "₽";
        public TimeSpan TimeAgo { get; set; } = TimeSpan.Zero;
    }
}