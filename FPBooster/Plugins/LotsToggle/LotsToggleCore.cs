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
    // Класс для хранения информации о лоте
    public class ToggleLotInfo
    {
        public string NodeId { get; set; } = "";
        public string OfferId { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Active { get; set; }
        
        public override string ToString() => Title;
    }

    public class LotsToggleCore
    {
        private HttpClient _client;
        private const string BaseUrl = "https://funpay.com";

        public LotsToggleCore(HttpClient client)
        {
            _client = client;
        }

        public void SetHttpClient(HttpClient client) => _client = client;

        // --- ИСПРАВЛЕНИЕ: Добавлен метод создания клиента ---
        public static HttpClient CreateClientWithCookie(string goldenKey)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (!string.IsNullOrEmpty(goldenKey))
            {
                handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", "funpay.com"));
                handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", ".funpay.com"));
            }

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            return client;
        }
        // ---------------------------------------------------

        public async Task<List<ToggleLotInfo>> FetchLotsAsync(string nodeId)
        {
            var results = new List<ToggleLotInfo>();
            var tradeUrl = $"{BaseUrl}/lots/{nodeId}/trade";

            try
            {
                var resp = await _client.GetAsync(tradeUrl);
                if (!resp.IsSuccessStatusCode) return results;

                var html = await resp.Content.ReadAsStringAsync();
                var offerEditUrls = new HashSet<string>();

                foreach (Match m in Regex.Matches(html, @"href\s*=\s*[""'](?<url>\/lots\/offerEdit\?[^""']+)[""']"))
                {
                    var url = m.Groups["url"].Value;
                    offerEditUrls.Add(url.StartsWith("http") ? url : $"{BaseUrl}{url}");
                }
                foreach (Match m in Regex.Matches(html, @"offerEdit\?[^""'>]*offer=(\d+)"))
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
                Console.WriteLine($"[LotsToggle] Fetch Error: {ex.Message}");
            }

            return results.GroupBy(l => l.OfferId).Select(g => g.First()).ToList();
        }

        private async Task<ToggleLotInfo?> ParseOfferEditAsync(string editUrl, string nodeId)
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
                var title = form.SelectSingleNode(".//input[starts-with(@name,'fields[summary]')]")?.GetAttributeValue("value", "") ?? $"Lot {offerId}";
                var activeInput = form.SelectSingleNode(".//input[@name='active']");
                bool isActive = activeInput != null && activeInput.Attributes["checked"] != null;

                if (!string.IsNullOrEmpty(offerId))
                    return new ToggleLotInfo { NodeId = nodeId, OfferId = offerId, Title = title, Active = isActive };
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
                    if (string.IsNullOrEmpty(name) || name == "active") continue;

                    var type = inp.GetAttributeValue("type", "").ToLower();
                    if (inp.Name == "select")
                    {
                        var sel = inp.SelectSingleNode(".//option[@selected]");
                        payload[name] = sel?.GetAttributeValue("value", "") ?? "";
                    }
                    else if (inp.Name == "textarea") payload[name] = inp.InnerText;
                    else if (type == "checkbox")
                    {
                        if (inp.Attributes["checked"] != null) payload[name] = inp.GetAttributeValue("value", "on");
                    }
                    else payload[name] = inp.GetAttributeValue("value", "");
                }

                if (active) payload["active"] = "on";

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

                var req = new HttpRequestMessage(HttpMethod.Post, action) { Content = new FormUrlEncodedContent(payload) };
                req.Headers.Referrer = new Uri(editUrl);

                var resp2 = await _client.SendAsync(req);
                return resp2.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}