using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FPBooster.FunPay
{
    public static class ProfileParser
    {
        public static HttpClient CreateClient(string goldenKey)
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

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            
            return client;
        }

        public static async Task<string?> GetUserIdAsync(HttpClient client)
        {
            try
            {
                var resp = await client.GetAsync("https://funpay.com/");
                if (!resp.IsSuccessStatusCode) return null;
                
                var html = await resp.Content.ReadAsStringAsync();
                
                var m = Regex.Match(html, @"data-app-data=""([^""]+)""");
                if (!m.Success) return null;
                
                var jsonBlob = WebUtility.HtmlDecode(m.Groups[1].Value);
                var m2 = Regex.Match(jsonBlob, @"""userId""\s*:\s*([0-9]+)");
                
                return m2.Success ? m2.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public static async Task<string> GetUserNameAsync(HttpClient client, string userId)
        {
            try 
            {
                var html = await client.GetStringAsync($"https://funpay.com/users/{userId}/");
                
                // ИСПРАВЛЕНО: Ищем ник в <span class="mr4">
                var m = Regex.Match(html, @"<span[^>]+class=""mr4""[^>]*>(.*?)</span>");
                
                if (m.Success)
                {
                    // Декодируем (чтобы Manavoid&lt;3 стало Manavoid<3) и убираем пробелы
                    return WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                }
                
                // Запасной вариант (если верстка отличается)
                m = Regex.Match(html, @"<span[^>]+class=""media-user-name""[^>]*>(.*?)</span>");
                if (m.Success)
                {
                    return WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                }
            }
            catch { }
            
            return $"User {userId}";
        }

        public static async Task<List<string>> ScanProfileForLots(HttpClient client, string userId)
        {
            var list = new List<string>();
            try
            {
                var html = await client.GetStringAsync($"https://funpay.com/users/{userId}/");
                var matches = Regex.Matches(html, @"/lots/(\d+)/trade");
                
                foreach (Match m in matches)
                {
                    if (m.Success)
                    {
                        var id = m.Groups[1].Value;
                        if (!list.Contains(id)) list.Add(id);
                    }
                }
            }
            catch { }
            return list;
        }
    }
}