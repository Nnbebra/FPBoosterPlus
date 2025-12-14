using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;

namespace FPBooster.ServerApi
{
    public class CloudApiClient
    {
        private static CloudApiClient? _instance;
        public static CloudApiClient Instance => _instance ??= new CloudApiClient();

        private const string BaseUrl = "https://fpbooster.shop"; 
        
        private string? _jwtToken;
        private readonly HttpClient _httpClient;

        public bool IsAuthorized => !string.IsNullOrEmpty(_jwtToken);

        private CloudApiClient()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(20)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FPBooster-Client/1.2");
        }

        // --- AUTH ---
        public bool TryLoadToken()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FPBooster", "session.dat");
                if (File.Exists(configPath))
                {
                    var token = File.ReadAllText(configPath).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        ApplyToken(token);
                        return true;
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CloudApi] Load Token Error: {ex.Message}"); }
            return false;
        }

        public void ApplyToken(string token)
        {
            _jwtToken = token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            if (_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
            _httpClient.DefaultRequestHeaders.Add("Cookie", $"user_auth={_jwtToken}");
        }

        // --- GENERAL HELPER ---
        private async Task<(bool Success, string Message)> PostDataAsync<T>(string url, T data)
        {
            if (!IsAuthorized) return (false, "Нет авторизации");
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, data);
                if (response.IsSuccessStatusCode) return (true, "Успешно");
                
                var err = await response.Content.ReadAsStringAsync();
                return (false, $"Ошибка сервера ({response.StatusCode}): {err}");
            }
            catch (Exception ex) { return (false, $"Ошибка сети: {ex.Message}"); }
        }

        // --- AUTO BUMP METHODS ---
        public async Task<(bool Success, string Message)> SetAutoBumpAsync(string key, List<string> nodes, bool active)
        {
            var payload = new { golden_key = key, node_ids = nodes, active = active };
            return await PostDataAsync("/api/plus/autobump/set", payload);
        }

        public async Task<bool> ForceCheckAutoBumpAsync()
        {
            if (!IsAuthorized) return false;
            try
            {
                var res = await _httpClient.PostAsync("/api/plus/autobump/force_check", null);
                return res.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<CloudStatusResponse?> GetAutoBumpStatusAsync()
        {
            if (!IsAuthorized) return null;
            try { return await _httpClient.GetFromJsonAsync<CloudStatusResponse>("/api/plus/autobump/status"); }
            catch { return null; }
        }

        // --- AUTO RESTOCK METHODS (NEW) ---
        public async Task<(bool Success, string Message)> SetAutoRestockAsync(string key, object items, bool active)
        {
            var payload = new { golden_key = key, items = items, active = active };
            return await PostDataAsync("/api/plus/autorestock/set", payload);
        }

        public async Task<bool> ForceCheckAutoRestockAsync()
        {
            if (!IsAuthorized) return false;
            try {
                var res = await _httpClient.PostAsync("/api/plus/autorestock/force_check", null);
                return res.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // --- DTO ---
        public class CloudStatusResponse
        {
            [JsonPropertyName("is_active")] public bool IsActive { get; set; }
            [JsonPropertyName("next_bump")] public DateTime? NextBump { get; set; }
            [JsonPropertyName("status_message")] public string StatusMessage { get; set; }
        }
    }
}