using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FPBooster.FunPay;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

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

        public AdvProfileStatCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;
        public void SetUseRealData(bool useRealData) => _useRealData = useRealData;

        private Dictionary<string, object> LoadEvents()
        {
            try
            {
                if (!File.Exists(StoreFile)) return new Dictionary<string, object>();
                var json = File.ReadAllText(StoreFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object>();
                var all = JsonElementToDictionary(doc.RootElement);

                var filtered = new Dictionary<string, object>(StringComparer.Ordinal);
                var idRegex = new Regex(@"^\d+$");
                foreach (var kv in all)
                {
                    if (idRegex.IsMatch(kv.Key))
                    {
                        filtered[kv.Key] = kv.Value;
                    }
                }

                return filtered;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] LoadEvents failed: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        private Dictionary<string, object> JsonElementToDictionary(JsonElement el)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var prop in el.EnumerateObject())
            {
                result[prop.Name] = JsonElementToObject(prop.Value);
            }
            return result;
        }

        private object JsonElementToObject(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    return JsonElementToDictionary(el);
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in el.EnumerateArray())
                        list.Add(JsonElementToObject(item));
                    return list;
                case JsonValueKind.String:
                    return el.GetString() ?? "";
                case JsonValueKind.Number:
                    if (el.TryGetDecimal(out var dec)) return dec;
                    if (el.TryGetInt64(out var l)) return l;
                    return el.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null!;
            }
        }

        private void SaveEvents(Dictionary<string, object> events)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(events, options);
                File.WriteAllText(StoreFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] SaveEvents failed: {ex.Message}");
            }
        }

        public async Task<Dictionary<string, object>> GenerateAdvProfileAsync()
        {
            if (_useRealData)
            {
                try
                {
                    var (orders, canWithdraw) = await FPBooster.FunPay.Stats.FetchRecentOrdersAsync(_client, 3);
                    
                    if (orders != null)
                    {
                        return ProcessRealData(orders, canWithdraw);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to get real data: {ex.Message}");
                }
            }
            // Исправление CS1998: оборачиваем синхронный результат в Task
            return await Task.FromResult(GenerateDemoData());
        }

        private Dictionary<string, object> ProcessRealData(List<FPBooster.FunPay.OrderItem> orders, Dictionary<string, string> canWithdraw)
        {
            try
            {
                var ((sales, refunds), (salesPrice, refundsPrice)) = FPBooster.FunPay.Stats.BucketByPeriod(orders);
                
                var events = LoadEvents();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                foreach (var order in orders)
                {
                    if (!string.IsNullOrEmpty(order.OrderId))
                    {
                        if (!events.ContainsKey(order.OrderId))
                        {
                            events[order.OrderId] = new Dictionary<string, object>
                            {
                                ["time"] = now,
                                ["price"] = order.Price,
                                ["currency"] = order.Currency
                            };
                        }
                    }
                }
                
                SaveEvents(events);

                var result = new Dictionary<string, object>
                {
                    ["sales"] = new Dictionary<string, object> 
                    { 
                        ["day"] = sales["day"],
                        ["week"] = sales["week"], 
                        ["month"] = sales["month"],
                        ["all"] = sales["all"] 
                    },
                    ["salesPrice"] = salesPrice.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                    ["refunds"] = new Dictionary<string, object> 
                    { 
                        ["day"] = refunds["day"],
                        ["week"] = refunds["week"], 
                        ["month"] = refunds["month"],
                        ["all"] = refunds["all"] 
                    },
                    ["refundsPrice"] = refundsPrice.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
                    ["canWithdraw"] = canWithdraw,
                    ["dataSource"] = "real"
                };

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] ProcessRealData failed: {ex.Message}");
                return GenerateDemoData();
            }
        }

        public async Task<Dictionary<string, string>> FetchQuickWithdrawAsync()
        {
            if (_useRealData)
            {
                try
                {
                    var (_, canWithdraw) = await FPBooster.FunPay.Stats.FetchRecentOrdersAsync(_client, 1);
                    return canWithdraw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] FetchQuickWithdrawAsync real data failed: {ex.Message}");
                }
            }

            // Исправление CS1998
            return await Task.FromResult(new Dictionary<string, string>
            {
                ["now"] = "12,450.50 ₽",
                ["EUR"] = "112.05 €", 
                ["RUB"] = "12,450.50 ₽",
                ["USD"] = "137.50 $"
            });
        }

        private Dictionary<string, object> GenerateDemoData()
        {
            var demoOrders = new List<FPBooster.FunPay.OrderItem>
            {
                new FPBooster.FunPay.OrderItem { OrderId = "12345", Status = "Completed", Price = 1050.50m, Currency = "₽" },
                new FPBooster.FunPay.OrderItem { OrderId = "12346", Status = "Completed", Price = 2500.00m, Currency = "₽" },
                new FPBooster.FunPay.OrderItem { OrderId = "12347", Status = "Refunded", Price = 1500.00m, Currency = "₽" },
                new FPBooster.FunPay.OrderItem { OrderId = "12348", Status = "Completed", Price = 25.00m, Currency = "$" },
                new FPBooster.FunPay.OrderItem { OrderId = "12349", Status = "Completed", Price = 20.00m, Currency = "€" }
            };
            
            var demoCanWithdraw = new Dictionary<string, string>
            {
                ["now"] = "12,450.50 ₽",
                ["EUR"] = "112.05 €", 
                ["RUB"] = "12,450.50 ₽",
                ["USD"] = "137.50 $"
            };

            var ((sales, refunds), (salesPrice, refundsPrice)) = FPBooster.FunPay.Stats.BucketByPeriod(demoOrders);
            
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
                ["canWithdraw"] = demoCanWithdraw,
                ["dataSource"] = "demo"
            };
        }
    }
}