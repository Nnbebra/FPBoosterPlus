using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text;

namespace FPBooster.Config
{
    public class ConfigManager
    {
        private static readonly string AppDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FPBooster");
        private static readonly string ConfigFile = Path.Combine(AppDataDir, "config.json");

        public class ConfigData
        {
            public string GoldenKey { get; set; } = "";
            public List<string> NodeIds { get; set; } = new();
            public string Theme { get; set; } = "Midnight Blue";
            public string UserName { get; set; } = "";
            public string LicenseStatus { get; set; } = "—";
        }

        public static ConfigData Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile, Encoding.UTF8);
                    var options = new JsonSerializerOptions { 
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = true
                    };
                    var result = JsonSerializer.Deserialize<ConfigData>(json, options);
                    
                    // Валидация загруженных данных
                    if (result != null)
                    {
                        result.NodeIds ??= new List<string>();
                        result.GoldenKey ??= "";
                        result.Theme ??= "Midnight Blue";
                        result.UserName ??= "";
                        result.LicenseStatus ??= "—";
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не прерываем работу
                System.Diagnostics.Debug.WriteLine($"[CONFIG] Ошибка загрузки: {ex.Message}");
            }
            
            // Возвращаем новый объект если загрузка не удалась
            return new ConfigData();
        }

        public static void Save(ConfigData data)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                
                // Валидация данных перед сохранением
                data.NodeIds ??= new List<string>();
                data.GoldenKey ??= "";
                data.Theme ??= "Midnight Blue";
                data.UserName ??= "";
                data.LicenseStatus ??= "—";
                
                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(ConfigFile, json, Encoding.UTF8);
                
                System.Diagnostics.Debug.WriteLine($"[CONFIG] Успешно сохранено: GoldenKey={!string.IsNullOrEmpty(data.GoldenKey)}, NodeIds={data.NodeIds.Count}, Theme={data.Theme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CONFIG] Критическая ошибка сохранения: {ex.Message}");
                throw;
            }
        }
    }
}