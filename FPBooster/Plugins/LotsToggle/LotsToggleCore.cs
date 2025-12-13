using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FPBooster.Plugins
{
    // Класс LotInfo используем общий или определяем здесь
    // Если он уже есть в AutoRestockCore.cs, компилятор может ругаться на дубликат.
    // Если ругается - удалите эту строку:
    // public record LotInfo(string NodeId, string OfferId, string Title, bool Active);

    public class LotsToggleCore
    {
        private HttpClient _client;
        private const string BaseUrl = "https://funpay.com";

        public LotsToggleCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;

        /// <summary>
        /// Получает список лотов. Логика взята из AutoRestockCore для максимальной надежности.
        /// </summary>
        public async Task<List<LotInfo>> FetchLotsAsync(string nodeId)
        {
            var results = new List<LotInfo>();
            var tradeUrl = $"{BaseUrl}/lots/{nodeId}/trade";

            try
            {
                var resp = await _client.GetAsync(tradeUrl);
                if (!resp.IsSuccessStatusCode) return results;

                // Читаем с правильной кодировкой
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var encoding = System.Text.Encoding.UTF8;
                try {
                    var charset = resp.Content.Headers.ContentType?.CharSet;
                    if (!string.IsNullOrEmpty(charset)) encoding = System.Text.Encoding.GetEncoding(charset);
                } catch { }
                var html = encoding.GetString(bytes);

                var offerEditUrls = new HashSet<string>();

                // Сбор ссылок (Regex из AutoRestockCore)
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

                // Парсим
                foreach (var editUrl in offerEditUrls)
                {
                    var lot = await ParseOfferEditAsync(editUrl, nodeId);
                    if (lot != null) results.Add(lot);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LotsToggle] Fetch Error: {ex.Message}");
            }

            return results.GroupBy(l => l.OfferId).Select(g => g.First()).ToList();
        }

        private async Task<LotInfo?> ParseOfferEditAsync(string editUrl, string nodeId)
        {
            try
            {
                var resp = await _client.GetAsync(editUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@class,'form-offer-editor')]");
                if (form == null) return null;

                var offerId = form.SelectSingleNode(".//input[@name='offer_id']")?.GetAttributeValue("value", "") ?? "";
                var nodeInForm = form.SelectSingleNode(".//input[@name='node_id']")?.GetAttributeValue("value", nodeId) ?? nodeId;
                
                var title = form.SelectSingleNode(".//input[starts-with(@name,'fields[summary]')]")?.GetAttributeValue("value", "") 
                            ?? $"Lot {offerId}";

                // Проверка активности
                var activeInput = form.SelectSingleNode(".//input[@name='active']");
                bool isActive = activeInput != null && activeInput.Attributes["checked"] != null;

                if (!string.IsNullOrEmpty(offerId))
                    return new LotInfo(nodeInForm, offerId, title, isActive);
            }
            catch { }
            return null;
        }

        public async Task<bool> ToggleLotStateAsync(string nodeId, string offerId, bool active)
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

                foreach (var inp in form.SelectNodes(".//input|.//select|.//textarea") ?? Enumerable.Empty<HtmlNode>())
                {
                    var name = inp.GetAttributeValue("name", null);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name == "active") continue; 

                    var type = inp.GetAttributeValue("type", "").ToLower();
                    if (inp.Name == "select")
                    {
                        var sel = inp.SelectSingleNode(".//option[@selected]");
                        payload[name] = sel?.GetAttributeValue("value", "") ?? "";
                    }
                    else if (inp.Name == "textarea")
                    {
                        payload[name] = inp.InnerText; 
                    }
                    else if (type == "checkbox")
                    {
                        if (inp.Attributes["checked"] != null)
                            payload[name] = inp.GetAttributeValue("value", "on");
                    }
                    else
                    {
                        payload[name] = inp.GetAttributeValue("value", "");
                    }
                }

                if (active) payload["active"] = "on";
                else if (payload.ContainsKey("active")) payload.Remove("active");

                foreach (var hidden in new[] { "csrf_token", "offer_id", "node_id" })
                {
                    if (!payload.ContainsKey(hidden))
                    {
                         var el = form.SelectSingleNode($".//input[@name='{hidden}']");
                         if (el != null) payload[hidden] = el.GetAttributeValue("value", "");
                    }
                }
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
                Console.WriteLine($"[LotsToggle] Save Error: {ex.Message}");
                return false;
            }
        }
    }
}