using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

// Псевдоним для устранения конфликта с WinForms HtmlDocument, если он есть в проекте
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FPBooster.FunPay
{
    public static class Stats
    {
        /// <summary>
        /// Загружает последние заказы с https://funpay.com/orders/trade
        /// </summary>
        public static async Task<(List<OrderItem> orders, Dictionary<string, string> canWithdraw)> FetchRecentOrdersAsync(HttpClient session, int maxPages = 1)
        {
            var orders = new List<OrderItem>();
            var canWithdraw = new Dictionary<string, string>();
            
            // Инициализируем ключи, чтобы не было ошибок при обращении
            canWithdraw["now"] = "0 ₽";
            canWithdraw["RUB"] = "0 ₽";
            canWithdraw["USD"] = "0 $";
            canWithdraw["EUR"] = "0 €";

            try
            {
                // 1. Пробуем получить баланс со страницы /chips/ (если доступна)
                try 
                {
                    var chipsHtml = await session.GetStringAsync("https://funpay.com/chips/");
                    var chipsDoc = new HtmlDocument();
                    chipsDoc.LoadHtml(chipsHtml);
                    
                    // Ищем плашку баланса
                    var balanceNode = chipsDoc.DocumentNode.SelectSingleNode("//span[contains(@class, 'badge-balance')]");
                    if (balanceNode != null)
                    {
                        canWithdraw["now"] = balanceNode.InnerText.Trim();
                    }
                } 
                catch 
                { 
                    // Игнорируем ошибки получения баланса, это не критично
                }

                // 2. Парсим заказы (только первую страницу для скорости, или больше если надо)
                // Для статистики профиля обычно достаточно последних 100-200 заказов
                var ordersUrl = "https://funpay.com/orders/trade";
                var ordersResponse = await session.GetAsync(ordersUrl);
                
                if (ordersResponse.IsSuccessStatusCode)
                {
                    var html = await ordersResponse.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Ищем все строки заказов (класс tc-item)
                    var rows = doc.DocumentNode.SelectNodes("//a[contains(@class, 'tc-item')]");
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            try 
                            {
                                var priceNode = row.SelectSingleNode(".//div[@class='tc-price']");
                                var statusNode = row.SelectSingleNode(".//div[@class='tc-status']");
                                var idNode = row.SelectSingleNode(".//div[@class='tc-order']");

                                if (priceNode != null && statusNode != null)
                                {
                                    // Очистка цены от символов валют и пробелов
                                    var priceRaw = priceNode.InnerText.Trim(); 
                                    var currency = "₽";
                                    
                                    if (priceRaw.Contains("$")) currency = "$";
                                    else if (priceRaw.Contains("€")) currency = "€";

                                    // Оставляем только цифры, запятые и точки
                                    var priceClean = Regex.Replace(priceRaw, @"[^\d,\.]", "").Replace(" ", "");
                                    
                                    // Пробуем распарсить число
                                    if (decimal.TryParse(priceClean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                                    {
                                        orders.Add(new OrderItem
                                        {
                                            OrderId = idNode?.InnerText?.Trim() ?? "???",
                                            Status = statusNode.InnerText.Trim(),
                                            Price = price,
                                            Currency = currency
                                        });
                                    }
                                }
                            } 
                            catch 
                            { 
                                // Пропускаем "битые" строки, чтобы не ломать весь список
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Логирование ошибки можно добавить здесь или выше
            }

            return (orders, canWithdraw);
        }

        /// <summary>
        /// Группирует заказы по статусам (продажи vs возвраты) и считает суммы.
        /// </summary>
        public static ((Dictionary<string, int> sales, Dictionary<string, int> refunds), (Dictionary<string, decimal> salesPrice, Dictionary<string, decimal> refundsPrice)) BucketByPeriod(List<OrderItem> orders)
        {
            // Инициализация словарей счетчиков
            var sales = new Dictionary<string, int> { ["all"] = 0, ["day"] = 0, ["week"] = 0, ["month"] = 0 };
            var refunds = new Dictionary<string, int> { ["all"] = 0, ["day"] = 0, ["week"] = 0, ["month"] = 0 };
            
            // Инициализация словарей сумм
            var salesPrice = new Dictionary<string, decimal> { ["all_₽"] = 0, ["all_$"] = 0, ["all_€"] = 0 };
            var refundsPrice = new Dictionary<string, decimal> { ["all_₽"] = 0, ["all_$"] = 0, ["all_€"] = 0 };

            foreach (var order in orders)
            {
                var s = order.Status.ToLower();
                
                // Определение типа заказа
                bool isRefund = s.Contains("возврат") || s.Contains("refund") || s.Contains("отменен");
                bool isCompleted = s.Contains("закрыт") || s.Contains("completed") || s.Contains("подтвержден") || s.Contains("оплачен");

                // Если статус "Ожидает" или "Wait" - не считаем в статистику завершенных
                if (!isRefund && !isCompleted) continue;

                // Ключ для валюты (например "all_₽")
                var currencyKey = $"all_{order.Currency}";

                if (isRefund) 
                {
                    refunds["all"]++;
                    
                    if (!refundsPrice.ContainsKey(currencyKey)) refundsPrice[currencyKey] = 0;
                    refundsPrice[currencyKey] += order.Price;
                } 
                else 
                {
                    sales["all"]++;
                    
                    if (!salesPrice.ContainsKey(currencyKey)) salesPrice[currencyKey] = 0;
                    salesPrice[currencyKey] += order.Price;
                }
            }
            
            return ((sales, refunds), (salesPrice, refundsPrice));
        }
    }

    /// <summary>
    /// Модель заказа для статистики
    /// </summary>
    public class OrderItem
    {
        public string OrderId { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Price { get; set; }
        public string Currency { get; set; } = "₽";
    }
}