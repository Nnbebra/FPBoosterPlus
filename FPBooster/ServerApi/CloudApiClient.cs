using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text; 
using System.Text.Json; 
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Linq;

namespace FPBooster.ServerApi
{
    public class CloudApiClient
    {
        private static CloudApiClient? _instance;
        public static CloudApiClient Instance => _instance ??= new CloudApiClient();

        // 🛑 ПРОВЕРЬТЕ АДРЕС СЕРВЕРА
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

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                // Важно: не добавляем сюда политику именования, управляем через атрибуты
            };

            // Dev Token
            string devToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI4IiwiZW1haWwiOiJkb2JyeW1heDcwQGdtYWlsLmNvbSIsImlhdCI6MTc2NjA3OTQwMiwiZXhwIjoyMDgxNDM5NDAyfQ.frAxKkPm9ILpvb-IdOIZmdzpTJMhilTk-CunrNYFVeQ";
            ApplyToken(devToken);
        }

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
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var str = await response.Content.ReadAsStringAsync();

                // Пытаемся десериализовать ответ, даже если код ошибки (сервер может вернуть детали)
                try 
                {
                    var resObj = JsonSerializer.Deserialize<BaseResponse>(str, _jsonOptions);
                    if (resObj != null)
                    {
                        // Если статус != 200, но JSON пришел - возвращаем его сообщение
                        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(resObj.Message))
                            resObj.Message = $"HTTP {response.StatusCode}";
                        
                        return resObj;
                    }
                } 
                catch { }

                if (response.IsSuccessStatusCode)
                    return new BaseResponse { Success = true, Message = "Успешно" };
                
                return new BaseResponse { Success = false, Message = $"Сервер ({response.StatusCode}): {str}" };
            }
            catch (Exception ex) 
            { 
                return new BaseResponse { Success = false, Message = $"Сеть: {ex.Message}" }; 
            }
        }

        // --- AUTO BUMP ---
        public async Task<BaseResponse> SetAutoBumpAsync(string key, List<string> nodes, bool active)
        {
            var request = new SetAutoBumpRequest
            {
                GoldenKey = key,
                NodeIds = nodes ?? new List<string>(),
                Active = active
            };
            return await PostDataAsync("/api/plus/autobump/set?app=pc", request);
        }

        public async Task<BaseResponse> ForceCheckAutoBumpAsync()
        {
            try
            {
                var res = await _httpClient.PostAsync("/api/plus/autobump/force_check?app=pc", null);
                var str = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode) return new BaseResponse { Success = true, Message = "Проверка запущена" };
                return new BaseResponse { Success = false, Message = str };
            }
            catch (Exception ex) { return new BaseResponse { Success = false, Message = ex.Message }; }
        }

        public async Task<CloudStatusResponse?> GetAutoBumpStatusAsync()
        {
            try { 
                var str = await _httpClient.GetStringAsync("/api/plus/autobump/status?app=pc");
                return JsonSerializer.Deserialize<CloudStatusResponse>(str, _jsonOptions);
            } catch { return null; }
        }

        // =================================================================
        // === AUTO RESTOCK (С ИСПРАВЛЕННОЙ СЕРИАЛИЗАЦИЕЙ) ===
        // =================================================================

        public async Task<BaseResponse> SaveAutoRestockAsync(string key, bool active, List<LotRestockConfig> lots)
        {
            // Используем DTO с атрибутами [JsonPropertyName]
            var request = new SetRestockRequest
            {
                GoldenKey = key,
                Active = active,
                Lots = lots
            };
            
            return await PostDataAsync("/api/plus/autorestock/set?app=pc", request);
        }

        public async Task<RestockStatusResponse?> GetAutoRestockStatusAsync()
        {
            return await GetJsonAsync<RestockStatusResponse>("/api/plus/autorestock/status?app=pc");
        }

        public async Task<FetchOffersResponse?> FetchRestockOffersAsync(string key, List<string> nodes)
        {
            try {
                // Здесь используем анонимный объект, так как он простой
                var payload = new { golden_key = key, node_ids = nodes };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var res = await _httpClient.PostAsync("/api/plus/autorestock/fetch_offers?app=pc", content);
                if (!res.IsSuccessStatusCode) return null;
                
                var str = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FetchOffersResponse>(str, _jsonOptions);
            } catch { return null; }
        }

        // --- DTO CLASSES ---
        
        // Auto Bump DTOs
        public class SetAutoBumpRequest
        {
            [JsonPropertyName("golden_key")] public string GoldenKey { get; set; } = "";
            [JsonPropertyName("node_ids")] public List<string> NodeIds { get; set; } = new();
            [JsonPropertyName("active")] public bool Active { get; set; }
        }

        // Base Response
        public class BaseResponse 
        { 
            [JsonPropertyName("success")] public bool Success { get; set; } 
            [JsonPropertyName("message")] public string Message { get; set; } = ""; 
            [JsonPropertyName("status")] public string Status { set { if (value == "success") Success = true; } }
        }

        // Cloud Status
        public class CloudStatusResponse
        {
            [JsonPropertyName("is_active")] public bool IsActive { get; set; }
            [JsonPropertyName("next_bump")] public DateTime? NextBump { get; set; }
            [JsonPropertyName("status_message")] public string? StatusMessage { get; set; }
            [JsonPropertyName("node_ids")] public List<string>? NodeIds { get; set; }
        }

        // --- Restock DTOs (ИСПРАВЛЕНО: Добавлены атрибуты JsonPropertyName) ---
        
        public class SetRestockRequest
        {
            [JsonPropertyName("golden_key")] public string GoldenKey { get; set; } = "";
            [JsonPropertyName("active")] public bool Active { get; set; }
            [JsonPropertyName("lots")] public List<LotRestockConfig> Lots { get; set; } = new();
        }

        public class LotRestockConfig
        {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("min_qty")] public int MinQty { get; set; }
            [JsonPropertyName("add_secrets")] public List<string> AddSecrets { get; set; } = new();
        }

        public class RestockStatusResponse
        {
            [JsonPropertyName("active")] public bool Active { get; set; }
            [JsonPropertyName("message")] public string Message { get; set; } = "";
            [JsonPropertyName("lots")] public List<LotStatusInfo> Lots { get; set; } = new();
        }

        public class LotStatusInfo 
        {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("min_qty")] public int MinQty { get; set; }
            [JsonPropertyName("keys_in_db")] public int KeysInDb { get; set; }
        }

        public class FetchOffersResponse : BaseResponse
        {
            [JsonPropertyName("data")] public List<FetchedOffer> Data { get; set; } = new();
        }

        public class FetchedOffer
        {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("valid")] public bool Valid { get; set; }
            [JsonPropertyName("error")] public string Error { get; set; } = "";
        }
    }
}