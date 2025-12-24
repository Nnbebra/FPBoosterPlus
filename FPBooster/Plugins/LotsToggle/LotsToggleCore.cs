#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FPBooster.Plugins
{
    public class ToggleLotInfo
    {
        public string NodeId { get; set; } = "";
        public string OfferId { get; set; } = "";
        
        // Сокращенное название для отображения в UI
        public string Title { get; set; } = ""; 
        
        // Полное название для поиска
        public string FullTitle { get; set; } = ""; 

        // Переопределение для корректного отображения в ComboBox
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

        /// <summary>
        /// Получает реальное название категории из H1
        /// </summary>
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
            catch 
            {
                return $"Раздел {nodeId}";
            }
        }

        /// <summary>
        /// Парсит офферы со страницы /trade, извлекая реальные названия
        /// </summary>
        public async Task<List<ToggleLotInfo>> GetLotsFromNode(string nodeId)
        {
            var result = new List<ToggleLotInfo>();
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
                        
                        // Формируем полное название для поиска
                        string fullDisplayTitle = string.IsNullOrEmpty(price) ? rawTitle : $"{rawTitle} ({price})";

                        result.Add(new ToggleLotInfo
                        {
                            NodeId = nodeId,
                            OfferId = offerId,
                            FullTitle = fullDisplayTitle,
                            Title = fullDisplayTitle // Сокращение будет во View
                        });
                    }
                }
            }
            catch { /* Игнорируем ошибки парсинга */ }
            return result;
        }

        /// <summary>
        /// Переключает состояние лота (Вкл/Выкл).
        /// enable = true (поставить галочку), enable = false (снять галочку)
        /// </summary>
        public async Task<bool> ToggleLotAsync(string offerId, bool enable)
        {
            try
            {
                // 1. Получаем страницу редактирования для сбора данных формы
                var editUrl = $"{BaseUrl}/lots/offerEdit?offer={offerId}"; 
                var resp = await _client.GetAsync(editUrl);
                if (resp.RequestMessage?.RequestUri != null) editUrl = resp.RequestMessage.RequestUri.ToString();
                if (!resp.IsSuccessStatusCode) return false;

                var html = await resp.Content.ReadAsStringAsync();
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var form = doc.DocumentNode.SelectSingleNode("//form[contains(@action, 'offerSave')]");
                if (form == null) return false;

                // 2. Собираем все input поля
                var inputs = form.SelectNodes(".//input");
                var payload = new Dictionary<string, string>();

                if (inputs != null)
                {
                    foreach (var input in inputs)
                    {
                        var name = input.GetAttributeValue("name", "");
                        var val = input.GetAttributeValue("value", "");
                        // Пропускаем чекбоксы, их мы обработаем отдельно
                        var type = input.GetAttributeValue("type", "").ToLower();
                        
                        if (!string.IsNullOrEmpty(name) && type != "checkbox") 
                        {
                            payload[name] = val;
                        }
                    }
                }
                
                // Также нужно собрать select и textarea, если они есть (обычно есть fields[summary][ru])
                var textareas = form.SelectNodes(".//textarea");
                if (textareas != null)
                {
                    foreach(var t in textareas)
                    {
                        var name = t.GetAttributeValue("name", "");
                        if(!string.IsNullOrEmpty(name)) payload[name] = t.InnerText;
                    }
                }
                
                var selects = form.SelectNodes(".//select");
                if (selects != null)
                {
                    foreach(var s in selects)
                    {
                        var name = s.GetAttributeValue("name", "");
                        var val = s.SelectSingleNode(".//option[@selected]")?.GetAttributeValue("value", "") 
                                  ?? s.SelectSingleNode(".//option")?.GetAttributeValue("value", ""); // fallback
                        if(!string.IsNullOrEmpty(name) && val != null) payload[name] = val;
                    }
                }

                // 3. Управляем галочкой "active"
                // Если хотим включить -> добавляем "active": "on"
                // Если хотим выключить -> удаляем ключ "active"
                if (enable)
                {
                    payload["active"] = "on";
                }
                else
                {
                    if (payload.ContainsKey("active")) payload.Remove("active");
                }

                // 4. Отправляем форму
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