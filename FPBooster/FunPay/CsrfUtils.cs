using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace FPBooster.FunPay
{
    public static class CsrfUtils
    {
        private const string BaseUrl = "https://funpay.com";

        private static readonly string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36";

        private static readonly HttpClient _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static CsrfUtils()
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.9");
        }

        /// <summary>
        /// Загружает HTML trade-страницы для указанного NodeID.
        /// </summary>
        public static async Task<string> GetTradeHtmlAsync(string nodeId)
        {
            var url = $"{BaseUrl}/lots/{nodeId}/trade";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri(url);

            var resp = await _client.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Извлекает CSRF-токен из HTML trade-страницы.
        /// </summary>
        public static async Task<string?> FetchCsrfAsync(string nodeId)
        {
            var html = await GetTradeHtmlAsync(nodeId);

            // Паттерны поиска токена
            string[] patterns =
            {
                @"data-app-data=""([^""]+)""",
                @"<meta[^>]+name=['""]csrf-token['""][^>]+content=['""]([^'""]+)['""]",
                @"<input[^>]+name=['""]csrf_token['""][^>]+value=['""]([^'""]+)['""]",
                @"window\.__NUXT__[^;]+['""]csrfToken['""]\s*:\s*['""]([^'""]+)['""]",
                @"data-csrf(?:-token)?=['""]([^'""]+)['""]",
                @"window\._csrf\s*=\s*['""]([^'""]+)['""]"
            };

            foreach (var p in patterns)
            {
                var m = Regex.Match(html, p);
                if (m.Success)
                {
                    if (p.Contains("data-app-data"))
                    {
                        var blob = HttpUtility.HtmlDecode(m.Groups[1].Value);
                        var t = Regex.Match(blob, @"""csrf-token""\s*:\s*""([^""]+)""");
                        if (t.Success) return t.Groups[1].Value;
                        t = Regex.Match(blob, @"""csrfToken""\s*:\s*""([^""]+)""");
                        if (t.Success) return t.Groups[1].Value;
                    }
                    else
                    {
                        return m.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Извлекает game_id из HTML trade-страницы.
        /// </summary>
        public static string? ExtractGameId(string html)
        {
            var m = Regex.Match(html, @"class=""btn[^""]*js-lot-raise""[^>]*data-game=""(\d+)""");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(html, @"data-game-id=""(\d+)""");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(html, @"data-game=""(\d+)""");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(html, @"data-app-data=""([^""]+)""");
            if (m.Success)
            {
                var blob = HttpUtility.HtmlDecode(m.Groups[1].Value);
                var t = Regex.Match(blob, @"""game-id""\s*:\s*(\d+)");
                if (t.Success) return t.Groups[1].Value;
            }

            return null;
        }
    }
}
