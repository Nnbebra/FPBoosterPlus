#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text; 
using System.Text.Json; 
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Management; // Обязательно убедитесь, что System.Management подключен в NuGet

namespace FPBooster.ServerApi
{
    public class CloudApiClient
    {
        private static CloudApiClient? _instance;
        public static CloudApiClient Instance => _instance ??= new CloudApiClient();

        private const string BaseUrl = "https://fpbooster.shop"; 
        
        private string? _jwtToken;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        
        // Путь к сессии Лаунчера
        private static readonly string SessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "FPBooster", 
            "session.dat");

        public bool IsAuthorized { get; private set; } = false;
        public string? CurrentHwid { get; private set; }

        private CloudApiClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
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
            };

            // Вместо хардкода токена инициализируем сессию
            InitializeSession();
        }

        public void InitializeSession()
        {
            try
            {
                // 1. HWID
                CurrentHwid = GenerateHwid();
                if (!_httpClient.DefaultRequestHeaders.Contains("X-HWID"))
                {
                    _httpClient.DefaultRequestHeaders.Add("X-HWID", CurrentHwid);
                }

                // 2. Читаем токен из файла лаунчера
                if (File.Exists(SessionPath))
                {
                    var token = File.ReadAllText(SessionPath).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        ApplyToken(token);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CloudApi] Session Init Error: {ex.Message}");
            }
        }

        public bool TryLoadToken() 
        {
            InitializeSession();
            return IsAuthorized;
        }

        public void ApplyToken(string token)
        {
            _jwtToken = token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            
            // Оригинальная логика cookie для совместимости
            if (_httpClient.DefaultRequestHeaders.Contains("Cookie"))
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
            _httpClient.DefaultRequestHeaders.Add("Cookie", $"user_auth={_jwtToken}");
            
            IsAuthorized = true;
        }

        // --- ГЕНЕРАЦИЯ HWID (Копия логики Лаунчера) ---
        private static string GenerateHwid()
        {
            try
            {
                string cpu = GetIdentifier("Win32_Processor", "ProcessorId");
                string hdd = GetIdentifier("Win32_DiskDrive", "SerialNumber");
                string board = GetIdentifier("Win32_BaseBoard", "SerialNumber");

                string rawId = $"{cpu}-{hdd}-{board}";

                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawId));
                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        builder.Append(bytes[i].ToString("x2"));
                    }
                    return builder.ToString();
                }
            }
            catch
            {
                return "UNKNOWN-HWID";
            }
        }

        private static string GetIdentifier(string wmiClass, string wmiProperty)
        {
            string result = "";
            try
            {
                ManagementClass mc = new ManagementClass(wmiClass);
                ManagementObjectCollection moc = mc.GetInstances();
                foreach (ManagementObject mo in moc)
                {
                    if (result == "")
                    {
                        try { result = mo[wmiProperty]?.ToString() ?? ""; break; } catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        // --- API HELPERS ---

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

                try 
                {
                    var resObj = JsonSerializer.Deserialize<BaseResponse>(str, _jsonOptions);
                    if (resObj != null)
                    {
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

        // --- МЕТОДЫ API (Восстановленные сигнатуры для плагинов) ---

        public async Task<BaseResponse> SetAutoBumpAsync(string key, List<string> nodes, bool active)
        {
            var request = new SetAutoBumpRequest { GoldenKey = key, NodeIds = nodes ?? new List<string>(), Active = active };
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

        public async Task<BaseResponse> SaveAutoRestockAsync(string key, bool active, List<LotRestockConfig> lots)
        {
            var request = new SetRestockRequest { GoldenKey = key, Active = active, Lots = lots };
            return await PostDataAsync("/api/plus/autorestock/set?app=pc", request);
        }

        public async Task<RestockStatusResponse?> GetAutoRestockStatusAsync()
        {
            return await GetJsonAsync<RestockStatusResponse>("/api/plus/autorestock/status?app=pc");
        }

        public async Task<FetchOffersResponse?> FetchRestockOffersAsync(string key, List<string> nodes)
        {
            try {
                var payload = new { golden_key = key, node_ids = nodes };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var res = await _httpClient.PostAsync("/api/plus/autorestock/fetch_offers?app=pc", content);
                if (!res.IsSuccessStatusCode) return null;
                
                var str = await res.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FetchOffersResponse>(str, _jsonOptions);
            } catch { return null; }
        }

        public async Task<List<FetchedOffer>> GetProductsAsync()
        {
            // Заглушка, если метод вызывается в MainWindow. 
            // Реальная реализация зависит от API сервера, но чтобы билд прошел, вернем пустой список.
            return new List<FetchedOffer>();
        }
        
        // --- МОДЕЛИ ДАННЫХ (Восстановленные) ---

        public class SetAutoBumpRequest
        {
            [JsonPropertyName("golden_key")] public string GoldenKey { get; set; } = "";
            [JsonPropertyName("node_ids")] public List<string> NodeIds { get; set; } = new();
            [JsonPropertyName("active")] public bool Active { get; set; }
        }

        public class BaseResponse 
        { 
            [JsonPropertyName("success")] public bool Success { get; set; } 
            [JsonPropertyName("message")] public string Message { get; set; } = ""; 
            [JsonPropertyName("status")] public string Status { set { if (value == "success") Success = true; } }
        }

        public class CloudStatusResponse
        {
            [JsonPropertyName("is_active")] public bool IsActive { get; set; }
            [JsonPropertyName("next_bump")] public DateTime? NextBump { get; set; }
            [JsonPropertyName("status_message")] public string? StatusMessage { get; set; }
            [JsonPropertyName("node_ids")] public List<string>? NodeIds { get; set; }
        }

        public class SetRestockRequest
        {
            [JsonPropertyName("golden_key")] public string GoldenKey { get; set; } = "";
            [JsonPropertyName("active")] public bool Active { get; set; }
            [JsonPropertyName("lots")] public List<LotRestockConfig> Lots { get; set; } = new();
        }

        public class LotRestockConfig {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("node_name")] public string NodeName { get; set; } = ""; 
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("min_qty")] public int MinQty { get; set; }
            [JsonPropertyName("add_secrets")] public List<string> AddSecrets { get; set; } = new();
            [JsonPropertyName("auto_enable")] public bool AutoEnable { get; set; } = true;
        }

        public class RestockStatusResponse
        {
            [JsonPropertyName("active")] public bool Active { get; set; }
            [JsonPropertyName("message")] public string Message { get; set; } = "";
            [JsonPropertyName("lots")] public List<LotStatusInfo> Lots { get; set; } = new();
            [JsonPropertyName("next_check")] public DateTime? NextCheck { get; set; } 
        }

        public class LotStatusInfo 
        {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("node_name")] public string NodeName { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("min_qty")] public int MinQty { get; set; }
            [JsonPropertyName("keys_in_db")] public int KeysInDb { get; set; }
            [JsonPropertyName("auto_enable")] public bool AutoEnable { get; set; }
            [JsonPropertyName("source_text")] public List<string> SourceText { get; set; } = new();
        }

        public class FetchOffersResponse : BaseResponse
        {
            [JsonPropertyName("data")] public List<FetchedOffer> Data { get; set; } = new();
        }

        public class FetchedOffer
        {
            [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
            [JsonPropertyName("node_name")] public string NodeName { get; set; } = "";
            [JsonPropertyName("offer_id")] public string OfferId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("valid")] public bool Valid { get; set; }
        }
    }
}