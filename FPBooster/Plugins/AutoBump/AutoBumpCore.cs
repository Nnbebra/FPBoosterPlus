using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FPBooster.Plugins
{
    public class AutoBumpCore
    {
        private HttpClient _client;
        private const string BaseUrl = "https://funpay.com";

        public AutoBumpCore(HttpClient client)
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
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = false // Используем системный прокси/IP
            };

            if (!string.IsNullOrEmpty(goldenKey))
            {
                handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", "funpay.com"));
                handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", ".funpay.com"));
            }

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru,en;q=0.9");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            return client;
        }

        /// <summary>
        /// Получает HTML страницы лота.
        /// </summary>
        private async Task<string> GetTradeHtmlAsync(string nodeId)
        {
            var url = $"{BaseUrl}/lots/{nodeId}/trade";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri(url);

            var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound) return "404";
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// За один запрос получает и CSRF, и GameID.
        /// </summary>
        public async Task<(string? Csrf, string? GameId, string Error)> GetLotInfoAsync(string nodeId)
        {
            try
            {
                var html = await GetTradeHtmlAsync(nodeId);
                if (html == "404") return (null, null, "Лот не найден (404)");
                
                if (html.Contains("Подождите"))
                {
                    var mWait = Regex.Match(html, @"class=""[^""]*ajax-alert-danger""[^>]*>(.*?)</div>", RegexOptions.Singleline);
                    if (mWait.Success)
                        return (null, null, $"Таймер: {mWait.Groups[1].Value.Trim()}");
                }

                // 1. Поиск CSRF
                string? csrf = null;
                var m = Regex.Match(html, @"data-app-data=""([^""]+)""");
                if (m.Success)
                {
                    var blob = WebUtility.HtmlDecode(m.Groups[1].Value);
                    var t = Regex.Match(blob, @"""csrf-token""\s*:\s*""([^""]+)""");
                    if (!t.Success) t = Regex.Match(blob, @"""csrfToken""\s*:\s*""([^""]+)""");
                    if (t.Success) csrf = t.Groups[1].Value;
                }
                
                if (string.IsNullOrEmpty(csrf))
                {
                    m = Regex.Match(html, @"name=[""']csrf_token[""'][^>]+value=[""']([^""']+)[""']");
                    if (m.Success) csrf = m.Groups[1].Value;
                }

                // 2. Поиск GameID
                string? gameId = null;
                m = Regex.Match(html, @"class=""btn[^""]*js-lot-raise""[^>]*data-game=""(\d+)""");
                if (m.Success) gameId = m.Groups[1].Value;

                if (string.IsNullOrEmpty(gameId))
                {
                    m = Regex.Match(html, @"data-game-id=""(\d+)""");
                    if (m.Success) gameId = m.Groups[1].Value;
                }

                if (string.IsNullOrEmpty(gameId))
                {
                    m = Regex.Match(html, @"data-app-data=""([^""]+)""");
                    if (m.Success)
                    {
                        var blob = WebUtility.HtmlDecode(m.Groups[1].Value);
                        var t = Regex.Match(blob, @"""game-id""\s*:\s*(\d+)");
                        if (t.Success) gameId = t.Groups[1].Value;
                    }
                }

                return (csrf, gameId, string.Empty);
            }
            catch (Exception ex)
            {
                return (null, null, $"Ошибка сети: {ex.Message}");
            }
        }

        /// <summary>
        /// Отправляет запрос на поднятие (Только POST, без лишних GET).
        /// </summary>
        public async Task<(bool Success, string Message)> BumpLotPostAsync(string nodeId, string gameId, string csrfToken)
        {
            try
            {
                var url = "https://funpay.com/lots/raise";
                var data = new Dictionary<string, string>
                {
                    ["game_id"] = gameId,
                    ["node_id"] = nodeId
                };
                if (!string.IsNullOrEmpty(csrfToken)) data["csrf_token"] = csrfToken;

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Referrer = new Uri($"https://funpay.com/lots/{nodeId}/trade");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("Origin", "https://funpay.com");
                if (!string.IsNullOrEmpty(csrfToken)) request.Headers.Add("X-CSRF-Token", csrfToken);

                request.Content = new FormUrlEncodedContent(data);

                var response = await _client.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return (false, $"HTTP {response.StatusCode}");

                try
                {
                    using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseText);
                    var root = jsonDoc.RootElement;
                    if (root.TryGetProperty("msg", out var msgProp))
                    {
                        var msg = msgProp.GetString();
                        // Если error нет или он 0 - успех
                        bool isError = root.TryGetProperty("error", out var errProp) && errProp.GetInt32() == 1;
                        return (!isError, msg ?? "Ответ сервера пуст");
                    }
                }
                catch 
                {
                    if (responseText.Contains("поднято")) return (true, "Поднято (HTML)");
                }

                return (false, $"Ответ FunPay: {responseText.Substring(0, Math.Min(50, responseText.Length))}");
            }
            catch (Exception ex)
            {
                return (false, $"Сбой POST: {ex.Message}");
            }
        }
    }
}