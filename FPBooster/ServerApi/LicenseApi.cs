using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FPBooster.ServerApi
{
    public static class LicenseApi
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private const string BASE_URL = "https://fpbooster.shop/api";

        public static async Task<Dictionary<string, object>> CheckLicense(string licenseKey)
        {
            try
            {
                var url = $"{BASE_URL}/license?license={Uri.EscapeDataString(licenseKey)}";
                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                           ?? new Dictionary<string, object>();

                return new Dictionary<string, object>
                {
                    ["status"]     = data.TryGetValue("status", out var status) ? status : "error",
                    ["expires"]    = data.TryGetValue("expires", out var expires) ? expires : null,
                    ["user"]       = data.TryGetValue("user", out var user) ? user : null,
                    ["user_uid"]   = data.TryGetValue("user_uid", out var uid) ? uid : null,
                    ["created"]    = data.TryGetValue("created", out var created) ? created : null,
                    ["last_check"] = data.TryGetValue("last_check", out var last) ? last : null
                };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["status"]  = "error",
                    ["message"] = $"Сетевая ошибка: {ex.Message}"
                };
            }
        }

        public static async Task<Dictionary<string, object>> UsePromocode(string licenseKey, string code)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("license_key", licenseKey),
                    new KeyValuePair<string, string>("code", code)
                });

                using var response = await _http.PostAsync($"{BASE_URL}/promocode/use", content).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>
                       {
                           ["ok"] = false,
                           ["error"] = "Пустой ответ сервера"
                       };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["ok"]    = false,
                    ["error"] = $"Сетевая ошибка: {ex.Message}"
                };
            }
        }

        public static async Task<Dictionary<string, object>> GetPromocodeInfo(string licenseKey)
        {
            try
            {
                var url = $"{BASE_URL}/promocode/info?license_key={Uri.EscapeDataString(licenseKey)}";
                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>
                       {
                           ["ok"] = false,
                           ["error"] = "Пустой ответ сервера"
                       };
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["ok"]    = false,
                    ["error"] = $"Сетевая ошибка: {ex.Message}"
                };
            }
        }
    }
}
