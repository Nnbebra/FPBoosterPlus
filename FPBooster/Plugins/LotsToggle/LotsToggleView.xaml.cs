using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace FPBooster.Plugins
{
    public partial class LotsToggleView : UserControl, IPlugin
    {
        private readonly LotsToggleCore _core;
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private string _goldenKey = "";
        private bool _isWorking = false;
        private bool _isInitialized = false;
        
        // Список лотов (кэш)
        private List<LotInfo> _loadedLots = new();

        public string Id => "lots_toggle";
        public string DisplayName => "Активация лотов";

        public LotsToggleView()
        {
            InitializeComponent();
            _core = new LotsToggleCore(new HttpClient());

            this.Loaded += (s, e) => {
                if (!_isInitialized) {
                    AnimateEntrance();
                    _isInitialized = true;
                }
            };
        }

        private void AnimateEntrance()
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        public UserControl GetView() => this;

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
            LogList.ItemsSource = _sharedLog;
            if (_sharedLog != null)
            {
                _sharedLog.CollectionChanged += (s, e) => {
                    if (LogList.Items.Count > 0)
                        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                };
            }
        }

        public void SetTheme(string themeKey) { }

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey ?? "";
            try
            {
                if (!string.IsNullOrEmpty(_goldenKey))
                    _core.SetHttpClient(AutoRestockCore.CreateClientWithCookie(_goldenKey));
            }
            catch (Exception ex)
            {
                Log($"[ERR] Ошибка Init: {ex.Message}", true);
            }

            var nodeList = nodes?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            NodeCombo.ItemsSource = nodeList;
            
            if (nodeList.Count > 0) 
            {
                NodeCombo.SelectedIndex = 0;
            }
            
            PluginStatus.Text = $"Готов ({nodeList.Count} разд.)";
        }

        private async void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var node = NodeCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(node)) return;

            OfferCombo.ItemsSource = null;
            _loadedLots.Clear();
            StatsGrid.Visibility = Visibility.Collapsed;
            SearchInput.Text = "";

            await LoadOffersForNode(node);
        }

        private void SpecificLotCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool isSpecific = SpecificLotCheck.IsChecked == true;
            OfferSelectPanel.Visibility = isSpecific ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtonsText();
        }

        // --- БОНУС: Фильтр ---
        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadedLots.Count == 0) return;

            var filter = SearchInput.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(filter))
            {
                OfferCombo.ItemsSource = _loadedLots;
            }
            else
            {
                var filtered = _loadedLots.Where(l => l.Title.ToLower().Contains(filter)).ToList();
                OfferCombo.ItemsSource = filtered;
            }
            
            if (OfferCombo.Items.Count > 0) OfferCombo.SelectedIndex = 0;
        }
        // ----------------------

        private void UpdateButtonsText()
        {
            if (SpecificLotCheck.IsChecked == true)
            {
                BtnActivate.Content = "✅ Включить (Выбранный)";
                BtnDeactivate.Content = "⛔ Выключить (Выбранный)";
            }
            else
            {
                BtnActivate.Content = "✅ Включить ВСЁ";
                BtnDeactivate.Content = "⛔ Выключить ВСЁ";
            }
        }

        private void UpdateStatsUI()
        {
            if (_loadedLots == null || _loadedLots.Count == 0) return;

            int active = _loadedLots.Count(x => x.Active);
            int inactive = _loadedLots.Count - active;
            
            StatActive.Text = active.ToString();
            StatInactive.Text = inactive.ToString();
            StatTotal.Text = _loadedLots.Count.ToString();
            StatsGrid.Visibility = Visibility.Visible;
        }

        private async Task LoadOffersForNode(string node)
        {
            OffersLoadingIndicator.Visibility = Visibility.Visible;
            PluginStatus.Text = "Загрузка...";
            
            try
            {
                var lots = await _core.FetchLotsAsync(node);
                _loadedLots = lots;
                
                // Сброс фильтра
                OnSearchTextChanged(null, null);

                // !ВАЖНО!: Указываем, какое свойство показывать (так как в XAML шаблон убран)
                // Это решает проблему пустого текста
                OfferCombo.DisplayMemberPath = "Title"; 
                
                if (_loadedLots.Count > 0) OfferCombo.SelectedIndex = 0;

                UpdateStatsUI();

                Log($"[INFO] Загружено {_loadedLots.Count} лотов.");
                PluginStatus.Text = "Список загружен";
            }
            catch (Exception ex)
            {
                Log($"[ERR] Не удалось загрузить лоты: {ex.Message}", true);
            }
            finally
            {
                OffersLoadingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e) => await RunToggleProcess(true);
        private async void BtnDeactivate_Click(object sender, RoutedEventArgs e) => await RunToggleProcess(false);

        private async Task RunToggleProcess(bool targetState)
        {
            var node = NodeCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(node)) {
                Log("[WARN] Выберите раздел!", true);
                return;
            }
            if (_isWorking) return;

            List<LotInfo> targetLots = new();
            bool isSpecific = SpecificLotCheck.IsChecked == true;

            if (isSpecific)
            {
                if (OfferCombo.SelectedItem is LotInfo selectedLot)
                {
                    targetLots.Add(selectedLot);
                }
                else
                {
                    Log("[WARN] Выберите лот из списка!", true);
                    return;
                }
            }
            else
            {
                if (_loadedLots.Count == 0)
                {
                    await LoadOffersForNode(node);
                    if (_loadedLots.Count == 0) return;
                }
                targetLots = _loadedLots; // Ссылка на весь список
            }

            _isWorking = true;
            UpdateUiState(false);
            ProgressPanel.Visibility = Visibility.Visible;
            ActionProgress.Value = 0;
            ActionProgress.Maximum = targetLots.Count;
            
            string actionName = targetState ? "ВКЛ" : "ВЫКЛ";
            Log($"[INFO] Старт {actionName}. Целей: {targetLots.Count}");

            int success = 0;
            int errors = 0;

            try
            {
                for (int i = 0; i < targetLots.Count; i++)
                {
                    var lot = targetLots[i];
                    ProgressText.Text = $"Обработано: {i + 1}/{targetLots.Count}";
                    ActionProgress.Value = i + 1;

                    bool ok = await _core.ToggleLotStateAsync(node, lot.OfferId, targetState);
                    if (ok)
                    {
                        Log($"[OK] {lot.Title} -> {actionName}", false);
                        success++;
                        
                        // ИСПРАВЛЕНИЕ: Обновляем статус локально для статистики
                        // Находим этот лот в глобальном списке _loadedLots по ID
                        var index = _loadedLots.FindIndex(x => x.OfferId == lot.OfferId);
                        if (index != -1)
                        {
                            // LotInfo - record, создаем новый экземпляр с обновленным статусом
                            _loadedLots[index] = new LotInfo(lot.NodeId, lot.OfferId, lot.Title, targetState);
                        }
                    }
                    else
                    {
                        Log($"[ERR] Ошибка: {lot.Title}", true);
                        errors++;
                    }

                    if (targetLots.Count > 1) await Task.Delay(500);
                }

                Log($"[FINISH] Успех: {success}, Ошибок: {errors}");
                PluginStatus.Text = "Готово";
                
                // СРАЗУ обновляем статистику и UI
                UpdateStatsUI();
                
                // Обновляем комбобокс, чтобы новые статусы применились к списку (если использовался фильтр)
                if (isSpecific)
                {
                    // Сохраняем выбранный элемент, чтобы не сбросился
                    var selectedId = (OfferCombo.SelectedItem as LotInfo)?.OfferId;
                    
                    // Передергиваем фильтр
                    OnSearchTextChanged(null, null);

                    // Пытаемся восстановить выбор
                    if (selectedId != null)
                    {
                         var newItem = _loadedLots.FirstOrDefault(x => x.OfferId == selectedId);
                         if (newItem != null) OfferCombo.SelectedItem = newItem;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERR] Критическая ошибка: {ex.Message}", true);
            }
            finally
            {
                _isWorking = false;
                UpdateUiState(true);
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateUiState(bool enabled)
        {
            BtnActivate.IsEnabled = enabled;
            BtnDeactivate.IsEnabled = enabled;
            NodeCombo.IsEnabled = enabled;
            SpecificLotCheck.IsEnabled = enabled;
            OfferCombo.IsEnabled = enabled;
            SearchInput.IsEnabled = enabled;
        }

        private void OnClearLog_Click(object sender, RoutedEventArgs e) => _sharedLog?.Clear();

        private void Log(string msg, bool isError = false)
        {
            if (_sharedLog == null) return;
            Brush color = Brushes.White;
            if (Application.Current.Resources["BrushText"] is SolidColorBrush themeBrush) color = themeBrush;
            
            if (isError || msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[OK]")) color = Brushes.LightGreen;
            else if (msg.Contains("[WARN]")) color = Brushes.Orange;

            _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = msg, Color = color });
        }
    }
}