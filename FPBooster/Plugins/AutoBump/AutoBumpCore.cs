using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

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
                UseProxy = false
            };

            if (!string.IsNullOrEmpty(goldenKey))
            {
                handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", "funpay.com"));
                handler.CookieContainer.Add(new Cookie("golden_key", goldenKey, "/", ".funpay.com"));
            }

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            return client;
        }

        // Вспомогательный метод для получения HTML
        private async Task<string> GetTradeHtmlAsync(string nodeId)
        {
            var url = $"{BaseUrl}/lots/{nodeId}/trade";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri(url);

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        // --- УЛУЧШЕННЫЙ МЕТОД ПОЛУЧЕНИЯ ИНФО (Возвращает RetryAfter) ---
        public async Task<(string? Csrf, string? GameId, string Error, TimeSpan? RetryAfter)> GetLotInfoAsync(string nodeId)
        {
            try
            {
                var html = await GetTradeHtmlAsync(nodeId);
                
                // Проверка на таймер (Подождите X ч. Y мин.)
                if (html.Contains("Подождите") || html.Contains("wait"))
                {
                    // Ищем блок с ошибкой
                    var mWait = Regex.Match(html, @"class=""[^""]*ajax-alert-danger""[^>]*>(.*?)</div>", RegexOptions.Singleline);
                    string errorText = mWait.Success ? mWait.Groups[1].Value.Trim() : "Таймер (неизвестное время)";
                    
                    // Парсим время из текста
                    TimeSpan? waitTime = ParseWaitTime(errorText);
                    return (null, null, errorText, waitTime);
                }

                // Поиск CSRF и GameID (как раньше)
                string? csrf = null;
                var m = Regex.Match(html, @"data-app-data=""([^""]+)""");
                if (m.Success)
                {
                    var blob = WebUtility.HtmlDecode(m.Groups[1].Value);
                    var t = Regex.Match(blob, @"""csrf-token""\s*:\s*""([^""]+)""");
                    if (t.Success) csrf = t.Groups[1].Value;
                }
                if (string.IsNullOrEmpty(csrf))
                {
                    m = Regex.Match(html, @"name=[""']csrf_token[""'][^>]+value=[""']([^""']+)[""']");
                    if (m.Success) csrf = m.Groups[1].Value;
                }

                string? gameId = null;
                m = Regex.Match(html, @"class=""btn[^""]*js-lot-raise""[^>]*data-game=""(\d+)""");
                if (m.Success) gameId = m.Groups[1].Value;
                
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

                return (csrf, gameId, string.Empty, null);
            }
            catch (Exception ex) 
            { 
                return (null, null, $"Ошибка доступа: {ex.Message}", null); 
            }
        }

        // Логика POST запроса на поднятие
        public async Task<(bool Success, string Message, TimeSpan? RetryAfter)> BumpLotPostAsync(string nodeId, string gameId, string csrfToken)
        {
            try
            {
                var url = "https://funpay.com/lots/raise";
                var data = new Dictionary<string, string> { ["game_id"] = gameId, ["node_id"] = nodeId };
                if (!string.IsNullOrEmpty(csrfToken)) data["csrf_token"] = csrfToken;

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Referrer = new Uri($"https://funpay.com/lots/{nodeId}/trade");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                if (!string.IsNullOrEmpty(csrfToken)) request.Headers.Add("X-CSRF-Token", csrfToken);
                
                request.Content = new FormUrlEncodedContent(data);

                var response = await _client.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode) return (false, $"HTTP {response.StatusCode}", null);

                try
                {
                    using var jsonDoc = JsonDocument.Parse(responseText);
                    var root = jsonDoc.RootElement;
                    if (root.TryGetProperty("msg", out var msgProp))
                    {
                        var msg = msgProp.GetString();
                        bool isError = false;
                        
                        if (root.TryGetProperty("error", out var errProp))
                        {
                            if (errProp.ValueKind == JsonValueKind.Number)
                                isError = errProp.GetInt32() == 1;
                            else if (errProp.ValueKind == JsonValueKind.True || errProp.ValueKind == JsonValueKind.False)
                                isError = errProp.GetBoolean();
                        }

                        // Если есть ошибка "Подождите", парсим время
                        if (isError && !string.IsNullOrEmpty(msg) && (msg.Contains("Подождите") || msg.Contains("wait")))
                        {
                            return (false, msg, ParseWaitTime(msg));
                        }
                        
                        return (!isError, msg ?? "Пустой ответ", null);
                    }
                }
                catch { }
                
                return (false, "Неизвестный ответ сервера", null);
            }
            catch (Exception ex) 
            { 
                return (false, $"Ошибка POST: {ex.Message}", null); 
            }
        }

        // --- НОВЫЙ МЕТОД: Парсинг времени из текста ---
        private TimeSpan? ParseWaitTime(string text)
        {
            try
            {
                // Ищем "3 ч." или "1 час"
                var matchHours = Regex.Match(text, @"(\d+)\s*(ч|h|час)");
                // Ищем "45 мин."
                var matchMinutes = Regex.Match(text, @"(\d+)\s*(м|min|мин)");

                int hours = matchHours.Success ? int.Parse(matchHours.Groups[1].Value) : 0;
                int minutes = matchMinutes.Success ? int.Parse(matchMinutes.Groups[1].Value) : 0;

                if (hours > 0 || minutes > 0)
                    return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
            }
            catch { }
            return null;
        }
    }
}