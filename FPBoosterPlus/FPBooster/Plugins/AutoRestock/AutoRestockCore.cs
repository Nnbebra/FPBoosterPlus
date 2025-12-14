using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FPBooster.Plugins
{
    public record LotInfo(string NodeId, string OfferId, string Title, bool Active);

    public class AutoRestockCore
    {
        private HttpClient _client;
        private const string BaseUrl = "https://funpay.com";

        public AutoRestockCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;

        public static HttpClient CreateClientWithCookie(string goldenKey)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", "funpay.com"));
            handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", ".funpay.com"));

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.9");
            return client;
        }

        public async Task<List<LotInfo>> FetchOffersByNodeAsync(string nodeId)
        {
            var results = new List<LotInfo>();
            var tradeUrl = $"{BaseUrl}/lots/{nodeId}/trade";

            try
            {
                var resp = await _client.GetAsync(tradeUrl);
                if (!resp.IsSuccessStatusCode) return results;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var encoding = System.Text.Encoding.UTF8;
                var charset = resp.Content.Headers.ContentType?.CharSet;
                if (!string.IsNullOrEmpty(charset))
                {
                    try { encoding = System.Text.Encoding.GetEncoding(charset); } catch { }
                }
                var html = encoding.GetString(bytes);
                var offerEditUrls = new HashSet<string>();

                // Сбор ссылок разными методами для надежности
                foreach (Match m in Regex.Matches(html, @"href\s*=\s*[""'](?<url>\/lots\/offerEdit\?[^""']+)[""']"))
                {
                    var url = m.Groups["url"].Value;
                    offerEditUrls.Add(url.StartsWith("http") ? url : $"{BaseUrl}{url}");
                }
                foreach (Match m in Regex.Matches(html, @"offerEdit\?[^""'>]*offer=(\d+)"))
                {
                    offerEditUrls.Add($"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={m.Groups[1].Value}");
                }
                foreach (Match m in Regex.Matches(html, @"data-offer-id\s*=\s*[""'](\d+)[""']"))
                {
                    offerEditUrls.Add($"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={m.Groups[1].Value}");
                }

                foreach (var editUrl in offerEditUrls)
                {
                    var lot = await ParseOfferEditAsync(editUrl, nodeId);
                    if (lot != null) results.Add(lot);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoRestock] Fetch Error: {ex.Message}");
            }

            return results.GroupBy(l => l.OfferId).Select(g => g.First()).ToList();
        }

        private async Task<LotInfo?> ParseOfferEditAsync(string editUrl, string nodeId)
        {
            try
            {
                var resp = await _client.GetAsync(editUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var html = await resp.Content.ReadAsStringAsync(); // Упрощено, обычно UTF8 работает
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@class,'form-offer-editor')]");
                if (form == null) return null;

                var offerId = form.SelectSingleNode(".//input[@name='offer_id']")?.GetAttributeValue("value", "") ?? "";
                var nodeInForm = form.SelectSingleNode(".//input[@name='node_id']")?.GetAttributeValue("value", nodeId) ?? nodeId;
                
                // Ищем название в полях summary
                var title = form.SelectSingleNode(".//input[starts-with(@name,'fields[summary]')]")?.GetAttributeValue("value", "") ?? 
                            $"Lot {offerId}";

                // Проверка активности (галочка active должна быть checked)
                var activeInput = form.SelectSingleNode(".//input[@name='active']");
                bool isActive = activeInput != null && activeInput.Attributes["checked"] != null;

                if (!string.IsNullOrEmpty(offerId))
                    return new LotInfo(nodeInForm, offerId, title, isActive);
            }
            catch { }
            return null;
        }

        public async Task<bool> RestockLotAsync(string goldenKey, string nodeId, string offerId,
                                       string itemText, int amount,
                                       bool autoDelivery, bool autoActivate)
        {
            try
            {
                var editUrl = $"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={offerId}";
                var resp = await _client.GetAsync(editUrl);
                if (!resp.IsSuccessStatusCode) return false;

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@class,'form-offer-editor')]");
                if (form == null) return false;

                var payload = new Dictionary<string, string>();

                // 1. Собираем все поля формы
                foreach (var inp in form.SelectNodes(".//input|.//select|.//textarea") ?? Enumerable.Empty<HtmlNode>())
                {
                    var name = inp.GetAttributeValue("name", null);
                    if (string.IsNullOrEmpty(name)) continue;

                    // Пропускаем наши целевые поля, мы их зададим вручную
                    if (name == "amount" || name == "active" || name == "auto_delivery" || name == "secrets") 
                        continue;

                    var type = inp.GetAttributeValue("type", "").ToLower();

                    if (inp.Name == "select")
                    {
                        var sel = inp.SelectSingleNode(".//option[@selected]");
                        payload[name] = sel?.GetAttributeValue("value", "") ?? "";
                    }
                    else if (type == "checkbox")
                    {
                        // Для остальных чекбоксов сохраняем состояние
                        if (inp.Attributes["checked"] != null)
                            payload[name] = inp.GetAttributeValue("value", "on");
                    }
                    else
                    {
                        payload[name] = inp.GetAttributeValue("value", "");
                    }
                }

                // 2. ПРИНУДИТЕЛЬНО устанавливаем наши значения
                
                // Текст товара
                payload["secrets"] = string.Join("\n", Enumerable.Repeat(itemText, amount));
                
                // Количество
                payload["amount"] = amount.ToString();

                // Автовыдача: Если true - добавляем, если false - удаляем (на всякий случай)
                if (autoDelivery) payload["auto_delivery"] = "on";
                else if (payload.ContainsKey("auto_delivery")) payload.Remove("auto_delivery");

                // Активация: ЛОГИКА ИСПРАВЛЕНА
                // FunPay ожидает наличие поля 'active', если лот активен. Если поля нет - лот деактивируется.
                if (autoActivate) payload["active"] = "on";
                else if (payload.ContainsKey("active")) payload.Remove("active");

                // Обязательные скрытые поля (убеждаемся что они есть)
                foreach (var hidden in new[] { "csrf_token", "offer_id", "node_id", "location" })
                {
                    if (!payload.ContainsKey(hidden))
                    {
                        var el = form.SelectSingleNode($".//input[@name='{hidden}']");
                        if (el != null) payload[hidden] = el.GetAttributeValue("value", "");
                    }
                }

                // Кнопка сохранения
                payload["save"] = "Сохранить";

                var action = form.GetAttributeValue("action", "/lots/offerSave");
                if (!action.StartsWith("http")) action = BaseUrl + action;

                var content = new FormUrlEncodedContent(payload);
                var req = new HttpRequestMessage(HttpMethod.Post, action) { Content = content };
                req.Headers.Referrer = new Uri(editUrl);

                var resp2 = await _client.SendAsync(req);
                var resultHtml = await resp2.Content.ReadAsStringAsync();

                return resp2.IsSuccessStatusCode && !resultHtml.ToLower().Contains("ошибка");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoRestock] Save Error: {ex.Message}");
                return false;
            }
        }
    }
}