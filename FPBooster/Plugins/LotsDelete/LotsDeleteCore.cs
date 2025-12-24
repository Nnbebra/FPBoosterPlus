using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FPBooster.Plugins
{
    public class PluginLotInfo
    {
        public string OfferId { get; set; } = "";
        public string NodeId { get; set; } = "";
        
        // Это сокращенное название для отображения в UI (ComboBox)
        public string Title { get; set; } = ""; 
        
        // Это полное название для поиска
        public string FullTitle { get; set; } = ""; 

        // ВАЖНО: Переопределение ToString() исправляет текст в поле выбора
        public override string ToString() => Title;
    }

    public class LotsDeleteCore
    {
        private HttpClient _client;
        private const string BaseUrl = "https://funpay.com";

        public LotsDeleteCore(HttpClient client)
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            return client;
        }

        public async Task<string> GetNodeNameAsync(string nodeId)
        {
            try
            {
                var url = $"{BaseUrl}/lots/{nodeId}/";
                var html = await _client.GetStringAsync(url);
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);
                var h1 = doc.DocumentNode.SelectSingleNode("//h1");
                return h1?.InnerText.Trim() ?? $"Раздел {nodeId}";
            }
            catch { return $"Раздел {nodeId}"; }
        }

        public async Task<List<PluginLotInfo>> GetLotsFromNode(string nodeId)
        {
            var result = new List<PluginLotInfo>();
            try
            {
                var url = $"{BaseUrl}/lots/{nodeId}/trade";
                var resp = await _client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return result;

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var offerNodes = doc.DocumentNode.SelectNodes("//a[contains(@class, 'tc-item')]");

                if (offerNodes != null)
                {
                    foreach (var node in offerNodes)
                    {
                        var offerId = node.GetAttributeValue("data-offer", "");
                        if (string.IsNullOrEmpty(offerId))
                        {
                            var href = node.GetAttributeValue("href", "");
                            var match = Regex.Match(href, @"offer=(\d+)");
                            if (match.Success) offerId = match.Groups[1].Value;
                        }

                        if (string.IsNullOrEmpty(offerId)) continue;

                        var descNode = node.SelectSingleNode(".//div[@class='tc-desc-text']");
                        var priceNode = node.SelectSingleNode(".//div[@class='tc-price']");

                        var rawTitle = descNode?.InnerText?.Trim() ?? $"Лот {offerId}";
                        var price = priceNode?.InnerText?.Trim() ?? "";
                        
                        // Сохраняем полное название для поиска
                        string fullDisplayTitle = string.IsNullOrEmpty(price) ? rawTitle : $"{rawTitle} ({price})";

                        result.Add(new PluginLotInfo
                        {
                            NodeId = nodeId,
                            OfferId = offerId,
                            FullTitle = fullDisplayTitle,
                            Title = fullDisplayTitle // Пока копируем, сократим во View
                        });
                    }
                }
            }
            catch { }
            return result;
        }

        public async Task<bool> DeleteLotAsync(string offerId)
        {
            try
            {
                var editUrl = $"{BaseUrl}/lots/offerEdit?offer={offerId}"; 
                var resp = await _client.GetAsync(editUrl);
                if (resp.RequestMessage?.RequestUri != null) editUrl = resp.RequestMessage.RequestUri.ToString();
                if (!resp.IsSuccessStatusCode) return false;

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@action, 'offerSave')]");
                if (form == null) return false;

                var inputs = form.SelectNodes(".//input");
                var payload = new Dictionary<string, string>();

                if (inputs != null)
                {
                    foreach (var input in inputs)
                    {
                        var name = input.GetAttributeValue("name", "");
                        var val = input.GetAttributeValue("value", "");
                        if (!string.IsNullOrEmpty(name)) payload[name] = val;
                    }
                }
                
                if (payload.ContainsKey("deleted")) payload.Remove("deleted"); 
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
    }
}