using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FPBooster.ServerApi; 

namespace FPBooster.FPBoosterPlus
{
    public class CloudAutoBumpCore
    {
        public class OpResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
        }

        public class AutoBumpStatus
        {
            public bool IsActive { get; set; }
            public string StatusText { get; set; }
            public DateTime? NextRun { get; set; }
            // Добавили список лотов
            public List<string> NodeIds { get; set; } = new List<string>();
        }

        public List<string> ParseNodeIds(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return new List<string>();
            return rawText.Split(new[] { ',', ' ', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim()).Where(s => Regex.IsMatch(s, @"^\d+$")).Distinct().ToList();
        }

        public async Task<OpResult> SaveSettingsAsync(string goldenKey, string rawNodes, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(goldenKey))
                return new OpResult { Success = false, Message = "Golden Key не указан!" };

            var cleanNodes = ParseNodeIds(rawNodes);
            if (isActive && cleanNodes.Count == 0)
                return new OpResult { Success = false, Message = "Список лотов пуст!" };

            try
            {
                var response = await CloudApiClient.Instance.SetAutoBumpAsync(goldenKey, cleanNodes, isActive);
                return new OpResult { Success = response.Success, Message = response.Success ? "Сохранено" : $"Ошибка: {response.Message}" };
            }
            catch (Exception ex)
            {
                return new OpResult { Success = false, Message = $"Сбой: {ex.Message}" };
            }
        }

        public async Task<OpResult> ForceRefreshAsync()
        {
            try
            {
                var response = await CloudApiClient.Instance.ForceCheckAutoBumpAsync();
                return new OpResult { Success = response.Success, Message = response.Message };
            }
            catch (Exception ex)
            {
                return new OpResult { Success = false, Message = $"Ошибка: {ex.Message}" };
            }
        }

        public async Task<AutoBumpStatus> GetStatusAsync()
        {
            try
            {
                var status = await CloudApiClient.Instance.GetAutoBumpStatusAsync();
                if (status == null) 
                    return new AutoBumpStatus { IsActive = false, StatusText = "Нет связи", NextRun = null };

                return new AutoBumpStatus 
                { 
                    IsActive = status.IsActive,
                    StatusText = status.StatusMessage ?? "Готов",
                    NextRun = status.NextBump,
                    // Передаем лоты, если сервер их вернул
                    NodeIds = status.NodeIds ?? new List<string>()
                };
            }
            catch
            {
                return new AutoBumpStatus { IsActive = false, StatusText = "Ошибка сети", NextRun = null };
            }
        }
    }
}