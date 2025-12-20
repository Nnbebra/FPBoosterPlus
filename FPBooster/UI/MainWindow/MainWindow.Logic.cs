using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using FPBooster.FunPay;
using FPBooster.ServerApi;
using FPBooster.UI;

// Псевдонимы для устранения конфликтов
using WpfButton = System.Windows.Controls.Button; 
using MediaBrushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;

namespace FPBooster
{
    public partial class MainWindow
    {
        private async void SaveGoldenKey_Click(object sender, RoutedEventArgs e)
        {
            var k = GoldenKeyInput.Text?.Trim();
            if (string.IsNullOrEmpty(k)) { ShowThemed("Ошибка", "Введите Golden Key!"); SetNodesInputEnabled(false); return; }

            // Явное приведение к WPF Button
            var btn = sender as WpfButton;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳"; }

            try
            {
                var client = ProfileParser.CreateClient(k);
                var userId = await ProfileParser.GetUserIdAsync(client);

                if (userId != null)
                {
                    var userName = await ProfileParser.GetUserNameAsync(client, userId);
                    
                    _cachedGoldenKey = k;
                    _cachedUserName = userName; 
                    
                    LicenseStatus.Text = $"Аккаунт: {userName}";
                    GoldenKeyMasked.Text = Mask(k);
                    
                    ShowThemed("Успех", $"Ключ принят!\nДобро пожаловать, {userName}!");
                    AppendLog($"[AUTH] Вход выполнен: {userName}", MediaBrushes.Lime);
                    
                    SetNodesInputEnabled(true);
                    TryPersistImmediate();
                }
                else
                {
                    SetNodesInputEnabled(false);
                    ShowThemed("Ошибка", "Невалидный Golden Key!\nПроверьте, правильно ли вы его скопировали.");
                    AppendLog("[ERR] Неверный ключ", MediaBrushes.IndianRed);
                }
            }
            catch (Exception ex)
            {
                SetNodesInputEnabled(false);
                ShowThemed("Ошибка сети", ex.Message);
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = "💾"; }
            }
        }

        private async void OnExtractNodesClick(object sender, RoutedEventArgs e)
        {
            if (!_isKeyValid) { ShowThemed("Доступ запрещен", "Сначала подтвердите Golden Key!"); return; }

            var btn = sender as WpfButton;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳"; }

            AppendLog("[INFO] Сканирование профиля...", MediaBrushes.Cyan);
            try
            {
                var client = ProfileParser.CreateClient(_cachedGoldenKey);
                var userId = await ProfileParser.GetUserIdAsync(client);
                
                if (string.IsNullOrEmpty(userId)) { AppendLog("[ERR] Ошибка доступа к профилю", MediaBrushes.IndianRed); return; }

                var nodes = await ProfileParser.ScanProfileForLots(client, userId);
                
                if (nodes.Count == 0) { AppendLog("[WARN] Активных разделов не найдено", MediaBrushes.Orange); return; }

                int added = 0;
                foreach (var nid in nodes) {
                    bool exists = false;
                    foreach (var item in NodeList.Items) if (item.ToString() == nid) { exists = true; break; }
                    
                    if (!exists) {
                        NodeList.Items.Add(nid);
                        added++;
                    }
                }

                NodeCount.Text = NodeList.Items.Count.ToString();
                AppendLog($"[SUCCESS] Найдено и добавлено разделов: {added}", MediaBrushes.Lime);
                if (added > 0) TryPersistImmediate();
            }
            catch (Exception ex) { AppendLog($"[ERR] {ex.Message}", MediaBrushes.IndianRed); }
            finally { if (btn != null) { btn.IsEnabled = true; btn.Content = "🔄"; } }
        }

        private void AddNode_Click(object sender, RoutedEventArgs e) => AddNodeInternal(NodeInput.Text);

        private void AddNodeInternal(string? text)
        {
            if (!_isKeyValid) { ShowThemed("Ошибка", "Сначала введите Golden Key!"); return; }

            var raw = text?.Trim() ?? "";
            var nid = "";
            
            if (Regex.IsMatch(raw, @"^\d+$")) nid = raw;
            else 
            {
                var m = Regex.Match(raw, @"/lots/(\d+)/");
                if (m.Success) nid = m.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(nid))
            {
                ShowThemed("Некорректные данные", "Введите только цифры ID раздела\nили полную ссылку на раздел.");
                return;
            }

            foreach (var item in NodeList.Items)
            {
                if (item.ToString() == nid)
                {
                    AppendLog($"[INFO] Раздел {nid} уже есть в списке", MediaBrushes.Gray);
                    NodeInput.Clear();
                    return;
                }
            }

            NodeList.Items.Add(nid);
            NodeCount.Text = NodeList.Items.Count.ToString();
            AppendLog($"[ADD] Раздел добавлен: {nid}", MediaBrushes.White);
            NodeInput.Clear();
            TryPersistImmediate();
        }

        public void SaveStore() => SaveStoreSync();
        public string GetGoldenKey() => GoldenKeyInput.Text?.Trim() ?? "";
        
        public List<string> GetActiveNodeIds()
        {
            return NodeList.Items.Cast<object>()
                   .Select(i => i?.ToString() ?? "")
                   .Where(s => !string.IsNullOrWhiteSpace(s))
                   .ToList();
        }

        private async Task SaveStoreAsync() { if (!_isLoaded) return; var cfg = GetConfig(); await Task.Run(() => { try { FPBooster.Config.ConfigManager.Save(cfg); } catch { } }); }
        private void SaveStoreSync() { if (!_isLoaded) return; try { FPBooster.Config.ConfigManager.Save(GetConfig()); } catch { } }
        private FPBooster.Config.ConfigManager.ConfigData GetConfig() => new() { GoldenKey = GoldenKeyInput.Text?.Trim() ?? "", NodeIds = NodeList.Items.Cast<object>().Select(i => i.ToString() ?? "").ToList(), Theme = ThemeManager.CurrentTheme, UserName = _cachedUserName };
        private void TryPersistImmediate() { _saveTimer.Stop(); _saveTimer.Start(); }
        
        private string Mask(string s) => s.Length <= 6 ? "***" : s.Substring(0,3)+"***"+s.Substring(s.Length-3);
        private void CopySelectedNodesToClipboard() { try { Clipboard.SetText(string.Join("\n", NodeList.SelectedItems.Cast<object>())); AppendLog("[COPY] Скопировано"); } catch {} }
        
        private void SetNodesInputEnabled(bool enabled)
        {
            _isKeyValid = enabled;
            NodeInput.IsEnabled = enabled;
            NodeInput.Opacity = enabled ? 1.0 : 0.5;
            if (!enabled && string.IsNullOrEmpty(_cachedUserName)) LicenseStatus.Text = "Ожидание ключа...";
        }
    }
}