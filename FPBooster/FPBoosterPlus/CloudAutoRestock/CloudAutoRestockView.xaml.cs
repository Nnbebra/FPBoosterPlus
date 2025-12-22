using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.IO; 
using FPBooster.ServerApi;
using FPBooster.Config;

using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
using CheckBox = System.Windows.Controls.CheckBox;

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudAutoRestockView : UserControl
    {
        public event Action NavigateBack;
        public ObservableCollection<FPBooster.MainWindow.LogEntry> Logs { get; private set; } = new ObservableCollection<FPBooster.MainWindow.LogEntry>();

        public class OfferViewModel
        {
            public string NodeId { get; set; } = "";
            public string NodeName { get; set; } = "";
            public string OfferId { get; set; } = "";
            public string Name { get; set; } = "";
            
            private int _minQty = 5;
            public int MinQty 
            { 
                get => _minQty; 
                set => _minQty = value > 500 ? 500 : value; 
            }
            
            public bool AutoEnable { get; set; } = true;
            public string KeysToAddRaw { get; set; } = ""; 
            public string StatusInfo { get; set; } = "Новый"; 
        }

        public class CategoryViewModel
        {
            public string NodeId { get; set; } = "";
            public string NodeName { get; set; } = "";
            public bool IsExpanded { get; set; } = true;
            public ObservableCollection<OfferViewModel> Offers { get; set; } = new();
        }

        private ObservableCollection<OfferViewModel> _allOffers = new();
        private ObservableCollection<CategoryViewModel> _categories = new();

        private DateTime _lastActionTime = DateTime.MinValue;
        private DateTime? _nextServerCheck;
        private DispatcherTimer _cooldownTimer; // Для кнопки
        private DispatcherTimer _uiUpdateTimer; // Для таймера следующей проверки
        private const string CACHE_FILE = "restock_nodes.json";

        public CloudAutoRestockView()
        {
            InitializeComponent();
            LogList.ItemsSource = Logs;
            ListCategories.ItemsSource = _categories;
            
            _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cooldownTimer.Tick += CooldownTick;

            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiUpdateTimer.Tick += UIUpdateTick;
            _uiUpdateTimer.Start();
            
            Loaded += async (s, e) => { 
                LoadLocalConfig(); 
                LoadCachedNodes(); 
                await SyncWithServer(); 
            };
        }

        // --- ТАЙМЕР UI ---
        private void UIUpdateTick(object? sender, EventArgs e)
        {
            if (_nextServerCheck.HasValue)
            {
                var diff = _nextServerCheck.Value - DateTime.Now;
                if (diff.TotalSeconds > 0)
                {
                    TxtStatus.Text = $"Проверка через: {diff.Hours:D2}:{diff.Minutes:D2}:{diff.Seconds:D2}";
                }
                else
                {
                    TxtStatus.Text = "Проверка выполняется...";
                }
            }
            else
            {
                TxtStatus.Text = "Ожидание данных...";
            }
        }

        // --- КЭШ ---
        private void SaveCachedNodes() { try { File.WriteAllText(CACHE_FILE, InputNodes.Text); } catch { } }
        private void LoadCachedNodes() { try { if (File.Exists(CACHE_FILE)) InputNodes.Text = File.ReadAllText(CACHE_FILE); } catch { } }

        // --- ИМПОРТ ---
        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            try 
            { 
                var mainWindow = Application.Current.MainWindow; 
                if (mainWindow == null) return; 
                var type = mainWindow.GetType(); 
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance; 
                
                var fieldGk = type.GetField("GoldenKeyInput", flags); 
                if (fieldGk?.GetValue(mainWindow) is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) 
                    InputKey.Password = tb.Text.Trim(); 
                
                var fieldNodes = type.GetField("NodeList", flags); 
                if (fieldNodes?.GetValue(mainWindow) is System.Windows.Controls.ListBox lb) 
                { 
                    var items = lb.Items.Cast<object>().Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)); 
                    InputNodes.Text = string.Join("\n", items); 
                }
                SaveCachedNodes();
                Log("Данные импортированы", Brushes.Cyan);
            } 
            catch (Exception ex) { Log($"Ошибка импорта: {ex.Message}", Brushes.Red); }
        }

        // --- КУЛДАУН ---
        private bool CheckCooldown()
        {
            var diff = DateTime.Now - _lastActionTime;
            if (diff.TotalSeconds < 40)
            {
                Log($"Подождите {40 - (int)diff.TotalSeconds} сек...", Brushes.Orange);
                return false;
            }
            return true;
        }

        private void StartCooldown()
        {
            _lastActionTime = DateTime.Now;
            _cooldownTimer.Start();
            CooldownTick(null, null);
        }

        private void CooldownTick(object? sender, EventArgs? e)
        {
            var diff = DateTime.Now - _lastActionTime;
            var remaining = 40 - (int)diff.TotalSeconds;
            
            if (remaining <= 0)
            {
                _cooldownTimer.Stop();
                BtnLoadOffers.Content = "🔍 ЗАГРУЗИТЬ ОФФЕРЫ";
                BtnLoadOffers.IsEnabled = true;
                BtnSave.Content = "💾 СОХРАНИТЬ КОНФИГУРАЦИЮ";
                BtnSave.IsEnabled = true;
                // Включаем свитч визуально (но не меняем состояние, так как это делается через биндинг или клик)
                SwitchActive.IsEnabled = true; 
            }
            else
            {
                BtnLoadOffers.IsEnabled = false;
                BtnSave.IsEnabled = false;
                // SwitchActive.IsEnabled = false; // Можно блокировать, но лучше просто отменять действие в клике
                BtnLoadOffers.Content = $"ЖДИТЕ {remaining} C...";
                BtnSave.Content = $"ЖДИТЕ {remaining} C...";
            }
        }

        // --- ГРУППИРОВКА ---
        private void RebuildCategories()
        {
            var expandedStates = _categories.ToDictionary(k => k.NodeId, v => v.IsExpanded);
            _categories.Clear();
            var groups = _allOffers.GroupBy(x => x.NodeId).OrderBy(g => { long.TryParse(g.Key, out long id); return id; });

            foreach (var g in groups)
            {
                var first = g.First();
                var catName = !string.IsNullOrEmpty(first.NodeName) ? first.NodeName : $"Категория {first.NodeId}";
                var catVm = new CategoryViewModel
                {
                    NodeId = first.NodeId, NodeName = catName,
                    IsExpanded = expandedStates.ContainsKey(first.NodeId) ? expandedStates[first.NodeId] : true,
                    Offers = new ObservableCollection<OfferViewModel>(g)
                };
                _categories.Add(catVm);
            }
        }

        // --- КНОПКИ ---
        private async void OnLoadOffersClick(object sender, RoutedEventArgs e)
        {
            if (!CheckCooldown()) return;
            var key = InputKey.Password;
            var nodes = InputNodes.Text.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (string.IsNullOrEmpty(key) || !nodes.Any()) { Log("Заполните данные!", Brushes.Orange); return; }

            SaveCachedNodes();
            StartCooldown();
            Log("Загрузка...", Brushes.Gray);

            try
            {
                var result = await CloudApiClient.Instance.FetchRestockOffersAsync(key, nodes);
                if (result != null && result.Success)
                {
                    int added = 0;
                    foreach (var fetched in result.Data)
                    {
                        if (fetched.Valid && !_allOffers.Any(x => x.OfferId == fetched.OfferId))
                        {
                            _allOffers.Add(new OfferViewModel {
                                NodeId = fetched.NodeId, NodeName = fetched.NodeName,
                                OfferId = fetched.OfferId, Name = fetched.Name,
                                MinQty = 5, AutoEnable = true
                            });
                            added++;
                        }
                    }
                    RebuildCategories();
                    Log($"Добавлено {added} офферов", Brushes.LightGreen);
                }
                else Log($"Ошибка: {result?.Message}", Brushes.Red);
            }
            catch (Exception ex) { Log($"Сбой: {ex.Message}", Brushes.Red); }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Проверка кулдауна
            if (!CheckCooldown()) 
            {
                // Если клик был по чекбоксу (свитчу), нужно вернуть его состояние обратно
                if (sender is CheckBox cb) 
                {
                    cb.IsChecked = !cb.IsChecked; // Инвертируем обратно
                }
                return; 
            }

            StartCooldown();
            Log("Сохранение...", Brushes.Gray);

            var apiList = new List<CloudApiClient.LotRestockConfig>();
            foreach (var vm in _allOffers)
            {
                var keys = vm.KeysToAddRaw.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).Select(k=>k.Trim()).ToList();
                apiList.Add(new CloudApiClient.LotRestockConfig {
                    NodeId = vm.NodeId, NodeName = vm.NodeName,
                    OfferId = vm.OfferId, Name = vm.Name, 
                    MinQty = vm.MinQty, AutoEnable = vm.AutoEnable, AddSecrets = keys
                });
            }

            var res = await CloudApiClient.Instance.SaveAutoRestockAsync(InputKey.Password, SwitchActive.IsChecked == true, apiList);
            if (res.Success)
            {
                Log("✅ Сохранено", Brushes.LightGreen);
                // После сохранения запускаем мгновенное обновление статуса, чтобы таймер сбросился
                await Task.Delay(1000); 
                await SyncWithServer(); 
            }
            else Log($"Ошибка: {res.Message}", Brushes.Red);
        }

        private async Task SyncWithServer()
        {
            try
            {
                var status = await CloudApiClient.Instance.GetAutoRestockStatusAsync();
                if (status != null)
                {
                    SwitchActive.IsChecked = status.Active;
                    
                    // Обновляем время следующей проверки
                    if (status.NextCheck.HasValue)
                        _nextServerCheck = status.NextCheck.Value.ToLocalTime(); // Важно: конвертируем в локальное время
                    else 
                        _nextServerCheck = null;

                    UpdatePowerCardVisuals();

                    foreach (var sLot in status.Lots)
                    {
                        var existing = _allOffers.FirstOrDefault(x => x.OfferId == sLot.OfferId);
                        var statusStr = sLot.KeysInDb > 0 ? "Активен" : "Не настроен";
                        string restoredText = "";
                        if (sLot.SourceText != null && sLot.SourceText.Count > 0)
                            restoredText = string.Join("\n", sLot.SourceText);

                        if (existing != null) {
                            existing.StatusInfo = statusStr;
                            existing.MinQty = sLot.MinQty;
                            existing.AutoEnable = sLot.AutoEnable;
                            if(!string.IsNullOrEmpty(sLot.NodeName)) existing.NodeName = sLot.NodeName;
                            
                            // Восстанавливаем текст, если он пуст у пользователя
                            if (string.IsNullOrEmpty(existing.KeysToAddRaw) && !string.IsNullOrEmpty(restoredText))
                                existing.KeysToAddRaw = restoredText;
                        } else {
                            _allOffers.Add(new OfferViewModel {
                                NodeId = sLot.NodeId, NodeName = sLot.NodeName,
                                OfferId = sLot.OfferId, Name = sLot.Name,
                                MinQty = sLot.MinQty, StatusInfo = statusStr,
                                AutoEnable = sLot.AutoEnable, KeysToAddRaw = restoredText
                            });
                        }
                    }
                    RebuildCategories();
                }
            }
            catch { }
        }

        private void UpdatePowerCardVisuals()
        {
            bool isRunning = SwitchActive.IsChecked == true;
            ActiveStatusText.Text = isRunning ? "СЕРВЕР РАБОТАЕТ" : "СЕРВЕР ОСТАНОВЛЕН";
            
            // СВЕЧЕНИЕ ЗЕЛЕНЫМ (Логика в коде, можно также через триггеры, но так проще управлять)
            if (isRunning)
            {
                ActiveStatusText.Foreground = Brushes.SpringGreen;
                ActiveStatusText.Effect = new System.Windows.Media.Effects.DropShadowEffect { 
                    Color = Colors.SpringGreen, BlurRadius = 15, ShadowDepth = 0, Opacity = 0.6 
                };
                PowerCardGlow.Opacity = 0.3;
            }
            else
            {
                ActiveStatusText.Foreground = Brushes.Gray;
                ActiveStatusText.Effect = null;
                PowerCardGlow.Opacity = 0.05;
            }
        }

        private void OnDeleteOfferClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is OfferViewModel vm) {
                _allOffers.Remove(vm);
                RebuildCategories();
            }
        }

        public void SetSharedLog(ObservableCollection<FPBooster.MainWindow.LogEntry> shared) { Logs = shared; LogList.ItemsSource = Logs; }
        private void LoadLocalConfig() { try { var c = ConfigManager.Load(); InputKey.Password = c.GoldenKey; } catch { } }
        private void Log(string m, Brush c) => Logs.Insert(0, new FPBooster.MainWindow.LogEntry { Text = $"[{DateTime.Now:HH:mm}] {m}", Color = c });
        private void OnClearLogClick(object s, RoutedEventArgs e) => Logs.Clear();
        private void OnBackClick(object s, RoutedEventArgs e) => NavigateBack?.Invoke();
    }
}