using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text; 
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

        // 🛑 ПРОВЕРЬТЕ АДРЕС! Для локального теста: http://127.0.0.1:8000
        private const string BaseUrl = "https://fpbooster.shop"; 
        
        private string? _jwtToken;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public bool IsAuthorized => true;

        private CloudApiClient()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FPBooster-Client/1.4");

            // Настройки JSON: игнорируем регистр букв, разрешаем комментарии
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            // ========================================================================
            // 🛑 DEV MODE: ВАШ ВЕЧНЫЙ ТОКЕН
            // ========================================================================
            string devToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI4IiwiZW1haWwiOiJkb2JyeW1heDcwQGdtYWlsLmNvbSIsImlhdCI6MTc2NjA3OTQwMiwiZXhwIjoyMDgxNDM5NDAyfQ.frAxKkPm9ILpvb-IdOIZmdzpTJMhilTk-CunrNYFVeQ";
            ApplyToken(devToken);
            // ========================================================================
        }

        // --- AUTH ---
        public bool TryLoadToken() => true;

        public void ApplyToken(string token)
        {
            _jwtToken = token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            if (_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
            _httpClient.DefaultRequestHeaders.Add("Cookie", $"user_auth={_jwtToken}");
        }

        // --- HELPER ---
        private async Task<T?> GetJsonAsync<T>(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return default;
                var str = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(str, _jsonOptions);
            }
            catch { return default; }
        }

        private async Task<BaseResponse> PostDataAsync<T>(string url, T data)
        {
            try
            {
                // Сериализуем данные с настройками
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var str = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try 
                    {
                        var resObj = JsonSerializer.Deserialize<BaseResponse>(str, _jsonOptions);
                        if (resObj != null) return resObj;
                    } 
                    catch { }
                    return new BaseResponse { Success = true, Message = "Успешно" };
                }
                
                // Если ошибка 422 или 500 - возвращаем её текст
                return new BaseResponse { Success = false, Message = $"Сервер ({response.StatusCode}): {str}" };
            }
            catch (Exception ex) 
            { 
                return new BaseResponse { Success = false, Message = $"Сеть: {ex.Message}" }; 
            }
        }

        // --- AUTO BUMP METHODS ---
        public async Task<BaseResponse> SetAutoBumpAsync(string key, List<string> nodes, bool active)
        {
            // ИСПОЛЬЗУЕМ СТРОГИЙ КЛАСС (DTO), чтобы избежать ошибок типов
            var request = new SetAutoBumpRequest
            {
                GoldenKey = key,
                NodeIds = nodes ?? new List<string>(),
                Active = active
            };
            
            return await PostDataAsync("/api/plus/autobump/set", request);
        }

        public async Task<BaseResponse> ForceCheckAutoBumpAsync()
        {
            try
            {
                var res = await _httpClient.PostAsync("/api/plus/autobump/force_check", null);
                var str = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode) 
                    return new BaseResponse { Success = true, Message = "Проверка запущена" };
                
                return new BaseResponse { Success = false, Message = str };
            }
            catch (Exception ex) 
            { 
                return new BaseResponse { Success = false, Message = ex.Message }; 
            }
        }

        public async Task<CloudStatusResponse?> GetAutoBumpStatusAsync()
        {
            try 
            { 
                var str = await _httpClient.GetStringAsync("/api/plus/autobump/status");
                return JsonSerializer.Deserialize<CloudStatusResponse>(str, _jsonOptions);
            }
            catch 
            { 
                return null; 
            }
        }

        // --- DTO CLASSES (Строгая типизация для общения с Python) ---
        
        public class SetAutoBumpRequest
        {
            [JsonPropertyName("golden_key")]
            public string GoldenKey { get; set; } = "";

            [JsonPropertyName("node_ids")]
            public List<string> NodeIds { get; set; } = new List<string>();

            [JsonPropertyName("active")]
            public bool Active { get; set; }
        }

        public class BaseResponse 
        { 
            [JsonPropertyName("success")]
            public bool Success { get; set; } 
            
            [JsonPropertyName("message")]
            public string Message { get; set; } = ""; 
            
            [JsonPropertyName("status")]
            public string Status { 
                set { if (value == "success") Success = true; } 
            }
        }

        public class CloudStatusResponse
        {
            [JsonPropertyName("is_active")] public bool IsActive { get; set; }
            [JsonPropertyName("next_bump")] public DateTime? NextBump { get; set; }
            [JsonPropertyName("status_message")] public string? StatusMessage { get; set; }
            [JsonPropertyName("node_ids")] public List<string>? NodeIds { get; set; } // <--- ДОБАВЛЕНО
        }

        // =========================================================================
        // === НОВЫЕ МЕТОДЫ (AUTO RESTOCK) - ДОБАВЛЕНЫ БЕЗ ИЗМЕНЕНИЯ СТАРЫХ ===
        // =========================================================================

        public async Task<BaseResponse> SaveAutoRestockAsync(string key, bool active, List<LotRestockConfig> lots)
        {
            // Используем старый добрый PostDataAsync
            return await PostDataAsync("/api/plus/autorestock/set", new { golden_key = key, active = active, lots = lots });
        }

        public async Task<RestockStatusResponse?> GetAutoRestockStatusAsync()
        {
            // Используем старый добрый GetJsonAsync
            return await GetJsonAsync<RestockStatusResponse>("/api/plus/autorestock/status");
        }

        public async Task<FetchOffersResponse?> FetchRestockOffersAsync(string key, List<string> nodes)
        {
            // Ручная реализация, чтобы не создавать новые хелперы и не путать код
            try {
                var payload = new { golden_key = key, node_ids = nodes };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var res = await _httpClient.PostAsync("/api/plus/autorestock/fetch_offers", content);
                if (!res.IsSuccessStatusCode) return null;
                
                var str = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FetchOffersResponse>(str, _jsonOptions);
            } catch { return null; }
        }

        // New DTOs
        public class LotRestockConfig {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("min_qty")] public int MinQty { get; set; }
            [JsonPropertyName("add_secrets")] public List<string> AddSecrets { get; set; } = new List<string>();
        }
        public class RestockStatusResponse {
            [JsonPropertyName("active")] public bool Active { get; set; }
            [JsonPropertyName("message")] public string Message { get; set; } = "";
            [JsonPropertyName("lots")] public List<LotStatusInfo> Lots { get; set; } = new List<LotStatusInfo>();
        }
        public class LotStatusInfo {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("min_qty")] public int MinQty { get; set; }
            [JsonPropertyName("keys_in_db")] public int KeysInDb { get; set; }
        }
        public class FetchOffersResponse : BaseResponse {
            [JsonPropertyName("data")] public List<FetchedOffer> Data { get; set; } = new List<FetchedOffer>();
        }
        public class FetchedOffer {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("valid")] public bool Valid { get; set; }
            [JsonPropertyName("error")] public string Error { get; set; } = "";
        }
    }
}