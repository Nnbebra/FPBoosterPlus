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
    // Определение LotInfo УДАЛЕНО, так как оно уже есть в AutoRestockCore.cs
    // Мы просто используем общее из пространства имен FPBooster.Plugins

    public class LotsDeleteCore
    {
        private HttpClient _client;
        private const string BaseUrl = "https://funpay.com";

        public LotsDeleteCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;

        /// <summary>
        /// Создаёт настроенный HttpClient с GoldenKey.
        /// </summary>
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

        private async Task<string?> GetUserIdAsync()
        {
            try
            {
                var resp = await _client.GetAsync($"{BaseUrl}/");
                if (!resp.IsSuccessStatusCode) return null;
                var html = await resp.Content.ReadAsStringAsync();

                var m = Regex.Match(html, @"data-app-data=""([^""]+)""");
                if (!m.Success) return null;

                var blob = WebUtility.HtmlDecode(m.Groups[1].Value);
                var m2 = Regex.Match(blob, @"""userId""\s*:\s*([0-9]+)");
                return m2.Success ? m2.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public async Task<List<string>> GetActiveNodeIdsFromProfileAsync()
        {
            var list = new List<string>();
            try
            {
                var userId = await GetUserIdAsync();
                if (string.IsNullOrEmpty(userId)) return list;

                var url = $"{BaseUrl}/users/{userId}/";
                var resp = await _client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return list;

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var links = doc.DocumentNode.SelectNodes("//a[contains(@href, '/lots/')]");
                if (links == null) return list;

                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (href.Contains("/trade"))
                    {
                        var match = Regex.Match(href, @"/lots/(\d+)/trade");
                        if (match.Success)
                        {
                            var id = match.Groups[1].Value;
                            if (!list.Contains(id)) list.Add(id);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Мощный метод получения лотов (аналог из AutoRestockCore).
        /// </summary>
        public async Task<List<LotInfo>> FetchOffersByNodeAsync(string nodeId)
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
                    if (resp.Content.Headers.ContentType?.CharSet != null)
                        encoding = System.Text.Encoding.GetEncoding(resp.Content.Headers.ContentType.CharSet);
                } catch { }
                var html = encoding.GetString(bytes);

                var offerEditUrls = new HashSet<string>();

                // 1. href links
                foreach (Match m in Regex.Matches(html, @"href\s*=\s*[""'](?<url>\/lots\/offerEdit\?[^""']+)[""']"))
                {
                    var url = m.Groups["url"].Value;
                    offerEditUrls.Add(url.StartsWith("http") ? url : $"{BaseUrl}{url}");
                }

                // 2. offer param
                foreach (Match m in Regex.Matches(html, @"offerEdit\?[^""'>]*offer=(\d+)"))
                    offerEditUrls.Add($"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={m.Groups[1].Value}");

                // 3. data-offer-id
                foreach (Match m in Regex.Matches(html, @"data-offer-id\s*=\s*[""'](\d+)[""']"))
                    offerEditUrls.Add($"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={m.Groups[1].Value}");

                // 4. data-offer
                foreach (Match m in Regex.Matches(html, @"data-offer\s*=\s*[""'](\d+)[""']"))
                    offerEditUrls.Add($"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={m.Groups[1].Value}");

                // Парсим каждый найденный лот
                foreach (var editUrl in offerEditUrls)
                {
                    var lot = await ParseOfferEditAsync(editUrl, nodeId);
                    if (lot != null) results.Add(lot);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LotsDeleteCore] Ошибка получения лотов: {ex.Message}");
            }

            // Уникализация
            return results.GroupBy(l => l.OfferId).Select(g => g.First()).ToList();
        }

        private async Task<LotInfo?> ParseOfferEditAsync(string editUrl, string nodeId)
        {
            try
            {
                var resp = await _client.GetAsync(editUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var encoding = System.Text.Encoding.UTF8;
                try {
                    if (resp.Content.Headers.ContentType?.CharSet != null)
                        encoding = System.Text.Encoding.GetEncoding(resp.Content.Headers.ContentType.CharSet);
                } catch { }
                var html = encoding.GetString(bytes);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@class,'form-offer-editor')]");
                if (form == null) return null;

                var offerId = form.SelectSingleNode(".//input[@name='offer_id']")?.GetAttributeValue("value", "") ?? "";
                var nodeInForm = form.SelectSingleNode(".//input[@name='node_id']")?.GetAttributeValue("value", nodeId) ?? nodeId;
                var title = form.SelectSingleNode(".//input[starts-with(@name,'fields[summary]')]")?.GetAttributeValue("value", $"Лот {offerId}") ?? $"Лот {offerId}";
                
                // Активен, если чекбокс active отмечен
                var active = form.SelectSingleNode(".//input[@name='active' and @checked]") != null;

                if (!string.IsNullOrEmpty(offerId))
                    return new LotInfo(nodeInForm, offerId, title, active);
            }
            catch { }
            return null;
        }

        public async Task<bool> DeleteLotAsync(string nodeId, string offerId)
        {
            try
            {
                var editUrl = $"{BaseUrl}/lots/offerEdit?node={nodeId}&offer={offerId}";
                var resp = await _client.GetAsync(editUrl);
                if (!resp.IsSuccessStatusCode) return false;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                var encoding = System.Text.Encoding.UTF8;
                try {
                    if (resp.Content.Headers.ContentType?.CharSet != null)
                        encoding = System.Text.Encoding.GetEncoding(resp.Content.Headers.ContentType.CharSet);
                } catch { }
                var html = encoding.GetString(bytes);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@class,'form-offer-editor')]");
                if (form == null) return false;

                var payload = new Dictionary<string, string>();
                foreach (var node in form.SelectNodes(".//input|.//select|.//textarea") ?? Enumerable.Empty<HtmlNode>())
                {
                    var name = node.GetAttributeValue("name", null);
                    if (string.IsNullOrEmpty(name)) continue;

                    if (node.Name == "select")
                    {
                        var sel = node.SelectSingleNode(".//option[@selected]");
                        payload[name] = sel?.GetAttributeValue("value", "") ?? "";
                    }
                    else if (node.Name == "textarea")
                    {
                        payload[name] = node.InnerText ?? "";
                    }
                    else
                    {
                        var type = node.GetAttributeValue("type", "").ToLower();
                        if (type == "checkbox")
                        {
                            if (node.Attributes["checked"] != null)
                                payload[name] = node.GetAttributeValue("value", "on");
                        }
                        else
                        {
                            payload[name] = node.GetAttributeValue("value", "");
                        }
                    }
                }

                // Флаг удаления
                payload["deleted"] = "1";

                var action = form.GetAttributeValue("action", "/lots/offerSave");
                if (!action.StartsWith("http")) action = BaseUrl + action;

                var content = new FormUrlEncodedContent(payload);
                var postReq = new HttpRequestMessage(HttpMethod.Post, action) { Content = content };
                postReq.Headers.Referrer = new Uri(editUrl);

                var resp2 = await _client.SendAsync(postReq);
                var resHtml = await resp2.Content.ReadAsStringAsync();

                return resp2.IsSuccessStatusCode && !resHtml.ToLower().Contains("ошибка");
            }
            catch { return false; }
        }

        public async Task<int> DeleteLotsListAsync(List<LotInfo> lots, Action<string> logger)
        {
            int successCount = 0;
            foreach (var lot in lots)
            {
                bool ok = await DeleteLotAsync(lot.NodeId, lot.OfferId);
                if (ok)
                {
                    successCount++;
                    logger?.Invoke($"[DEL] Удален: {lot.Title} (ID: {lot.OfferId})");
                }
                else
                {
                    logger?.Invoke($"[ERR] Ошибка удаления: {lot.OfferId}");
                }
                await Task.Delay(450); 
            }
            return successCount;
        }
    }
}