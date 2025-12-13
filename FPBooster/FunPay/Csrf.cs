using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using FPBooster.FunPay;

namespace FPBooster.FunPay
{
    public static class Csrf
    {
        private const string BaseUrl = "https://funpay.com";

        /// <summary>
        /// Проверяет GoldenKey на FunPay и возвращает имя аккаунта.
        /// </summary>
        public static async Task<(bool ok, string? userName)> VerifyGoldenKeyAsync(HttpClient client)
        {
            var url = "https://funpay.com/";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return (false, null);

            var html = await resp.Content.ReadAsStringAsync();
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var body = doc.DocumentNode.SelectSingleNode("//body");
            if (body == null) return (false, null);

            var data = body.GetAttributeValue("data-app-data", null);
            if (string.IsNullOrEmpty(data)) return (false, null);

            var blob = System.Net.WebUtility.HtmlDecode(data);
            try
            {
                var parsed = JsonDocument.Parse(blob);

                if (parsed.RootElement.TryGetProperty("userId", out var uid))
                {
                    var userId = uid.GetInt32();

                    // Если userId == 0 → ключ недействителен
                    if (userId <= 0)
                        return (false, null);

                    // Загружаем страницу профиля
                    var profileUrl = $"https://funpay.com/users/{userId}/";
                    var resp2 = await client.GetAsync(profileUrl);
                    if (!resp2.IsSuccessStatusCode) return (true, $"User {userId}");

                    var html2 = await resp2.Content.ReadAsStringAsync();
                    var doc2 = new HtmlAgilityPack.HtmlDocument();
                    doc2.LoadHtml(html2);

                    var nameNode = doc2.DocumentNode.SelectSingleNode("//h1") ??
                                doc2.DocumentNode.SelectSingleNode("//div[@class='user-name']");

                    var userName = nameNode?.InnerText?.Trim();

                    return (true, string.IsNullOrEmpty(userName) ? $"User {userId}" : userName);
                }

                return (false, null);
            }
            catch
            {
                return (false, null);
            }
        }



    }
}
