#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FPBooster.Plugins;

using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;

namespace FPBooster.Plugins
{
    public class UiNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name; 
    }

    public partial class LotsDeleteView : UserControl, IPlugin
    {
        private readonly LotsDeleteCore _core;
        private string _goldenKey = "";
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        
        private List<string> _rawNodeIds = new();
        private ObservableCollection<UiNode> _uiNodes = new(); 
        
        // Список лотов для текущего раздела
        private List<PluginLotInfo> _allLotsInNode = new();
        
        private TaskCompletionSource<bool>? _confirmTcs;

        public string Id => "lots_delete";
        public string DisplayName => "Удаление лотов";
        public UserControl GetView() => this;

        public LotsDeleteView()
        {
            InitializeComponent();
            _core = new LotsDeleteCore(new HttpClient());

            // ВАЖНО: Сбрасываем состояние при уходе со страницы плагина
            this.Unloaded += (s, e) => ResetState();
        }

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            // Также сбрасываем состояние при новой инициализации
            ResetState();

            _goldenKey = goldenKey;
            _rawNodeIds = nodes.ToList();

            if (!string.IsNullOrEmpty(_goldenKey))
            {
                var client = LotsDeleteCore.CreateClientWithCookie(_goldenKey);
                _core.SetHttpClient(client);
                
                Dispatcher.Invoke(() => 
                {
                    NodeCombo.ItemsSource = _uiNodes;
                    NodeCombo.DisplayMemberPath = "Name";
                    
                    _uiNodes.Clear();
                    foreach(var id in _rawNodeIds)
                    {
                        _uiNodes.Add(new UiNode { Id = id, Name = $"Раздел {id}" });
                    }
                });

                LoadNodeNamesAsync();
            }
        }

        /// <summary>
        /// Полный сброс UI в исходное состояние
        /// </summary>
        private void ResetState()
        {
            // 1. Сброс выбора раздела
            NodeCombo.SelectedItem = null;
            NodeCombo.SelectedIndex = -1;

            // 2. Очистка данных
            _allLotsInNode.Clear();
            CategoryCombo.ItemsSource = null;
            CategoryCombo.SelectedItem = null;

            // 3. Сброс полей ввода
            SearchInput.Text = "";
            ChbSingleMode.IsChecked = false;
            ChbSingleMode.IsEnabled = false;

            // 4. Скрытие панелей
            OfferSelectPanel.Visibility = Visibility.Collapsed;

            // 5. Блокировка кнопок
            BtnDeleteCategory.IsEnabled = false;
            BtnDeleteAll.IsEnabled = false;
            BtnDeleteAll.Content = "💀 УДАЛИТЬ ВСЕ";

            // 6. Сброс статуса
            if (PluginStatus != null)
            {
                PluginStatus.Text = "READY";
                try {
                    // ИСПРАВЛЕНИЕ ОШИБКИ: Явно указываем System.Windows.Media.Color
                    PluginStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xF2, 0xB2));
                } catch {
                    PluginStatus.Foreground = Brushes.LightGreen;
                }
            }
        }

        private async void LoadNodeNamesAsync()
        {
            foreach (var node in _uiNodes)
            {
                var realName = await _core.GetNodeNameAsync(node.Id);
                Dispatcher.Invoke(() => 
                {
                    node.Name = realName;
                    NodeCombo.Items.Refresh();
                });
                await Task.Delay(200); 
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
        }

        public void SetTheme(string themeKey) { }

        // --- ЛОГИКА UI ---

        private async void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodeCombo.SelectedItem is UiNode node)
            {
                // Сброс при смене раздела
                CategoryCombo.ItemsSource = null;
                SearchInput.Text = "";
                
                ChbSingleMode.IsChecked = false;
                ChbSingleMode.IsEnabled = false; 
                OfferSelectPanel.Visibility = Visibility.Collapsed;

                BtnDeleteCategory.IsEnabled = false;
                BtnDeleteAll.IsEnabled = false;
                
                UpdateStatus($"Загрузка: {Truncate(node.Name, 20)}...", Brushes.Yellow);

                try
                {
                    var lots = await _core.GetLotsFromNode(node.Id);
                    
                    foreach(var lot in lots)
                    {
                        lot.Title = Truncate(lot.FullTitle, 55); 
                    }

                    _allLotsInNode = lots;
                    CategoryCombo.ItemsSource = _allLotsInNode;
                    
                    if (lots.Count > 0)
                    {
                        ChbSingleMode.IsEnabled = true; 
                        BtnDeleteAll.IsEnabled = true;
                        BtnDeleteAll.Content = $"💀 Удалить все ({lots.Count})";
                        UpdateStatus($"Найдено: {lots.Count}", Brushes.LightGreen);
                    }
                    else
                    {
                        BtnDeleteAll.Content = "💀 УДАЛИТЬ ВСЕ";
                        UpdateStatus("Нет лотов", Brushes.White);
                    }
                }
                catch (Exception ex)
                {
                    AppendPluginLog($"[ERR] {ex.Message}");
                    UpdateStatus("Ошибка", Brushes.IndianRed);
                }
            }
        }

        private void OnSingleModeChanged(object sender, RoutedEventArgs e)
        {
            bool isChecked = ChbSingleMode.IsChecked == true;
            
            OfferSelectPanel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
            
            if (!isChecked)
            {
                CategoryCombo.SelectedItem = null;
                SearchInput.Text = "";
                BtnDeleteCategory.IsEnabled = false;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (CategoryCombo.ItemsSource == null) return;

            string filterText = SearchInput.Text;
            
            ICollectionView view = CollectionViewSource.GetDefaultView(CategoryCombo.ItemsSource);
            if (view == null) return;

            view.Filter = (obj) =>
            {
                if (string.IsNullOrEmpty(filterText)) return true;
                if (obj is PluginLotInfo lot)
                {
                    return lot.FullTitle.Contains(filterText, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            };
            
            view.Refresh();
            
            if (!string.IsNullOrEmpty(filterText))
            {
                CategoryCombo.IsDropDownOpen = true;
            }
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnDeleteCategory.IsEnabled = ChbSingleMode.IsChecked == true && CategoryCombo.SelectedItem != null;
        }

        private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
        {
            if (CategoryCombo.SelectedItem is PluginLotInfo selectedLot)
            {
                bool confirmed = await ShowConfirmOverlay($"Удалить лот:\n{Truncate(selectedLot.FullTitle, 100)}?");
                if (!confirmed) return;

                await RunDeleteProcess(new List<PluginLotInfo> { selectedLot });
            }
        }

        private async void OnDeleteAllClick(object sender, RoutedEventArgs e)
        {
            if (_allLotsInNode.Count > 0)
            {
                bool confirmed = await ShowConfirmOverlay($"ВНИМАНИЕ!\nВы собираетесь удалить ВСЕ ({_allLotsInNode.Count}) лоты в этом разделе.\nПродолжить?");
                if (!confirmed) return;
                
                await RunDeleteProcess(_allLotsInNode);
                return;
            }
            
            if (_rawNodeIds.Count > 0 && NodeCombo.SelectedIndex == -1)
            {
                 bool confirmed = await ShowConfirmOverlay($"ВНИМАНИЕ! ГЛОБАЛЬНАЯ ОЧИСТКА.\nБудут удалены лоты во ВСЕХ разделах ({_rawNodeIds.Count} шт).\nЭто действие необратимо.");
                 if (!confirmed) return;

                 NodeCombo.IsEnabled = false;
                 ChbSingleMode.IsEnabled = false;
                 SearchInput.IsEnabled = false;
                 CategoryCombo.IsEnabled = false;
                 BtnDeleteAll.IsEnabled = false;
                 
                 try 
                 {
                    int totalDeleted = 0;
                    foreach (var nodeId in _rawNodeIds)
                    {
                        UpdateStatus($"Сканирование {nodeId}...", Brushes.Orange);
                        var lots = await _core.GetLotsFromNode(nodeId);
                        
                        if (lots.Count > 0)
                        {
                            foreach (var lot in lots)
                            {
                                bool ok = await _core.DeleteLotAsync(lot.OfferId);
                                if (ok)
                                {
                                    totalDeleted++;
                                    AppendPluginLog($"[DEL] Удален: {Truncate(lot.FullTitle, 30)}");
                                }
                                await Task.Delay(450);
                            }
                        }
                        await Task.Delay(500);
                    }
                    AppendPluginLog($"[FINISH] Глобальная очистка. Удалено: {totalDeleted}");
                    UpdateStatus("Готово", Brushes.LightGreen);
                 }
                 catch (Exception ex)
                 {
                    AppendPluginLog($"[ERR] {ex.Message}");
                 }
                 finally
                 {
                    ResetState();
                    NodeCombo.IsEnabled = true; 
                 }
            }
        }

        private async Task RunDeleteProcess(List<PluginLotInfo> lots)
        {
            NodeCombo.IsEnabled = false;
            ChbSingleMode.IsEnabled = false;
            SearchInput.IsEnabled = false;
            CategoryCombo.IsEnabled = false;
            BtnDeleteAll.IsEnabled = false;
            BtnDeleteCategory.IsEnabled = false;

            try
            {
                UpdateStatus("Удаление...", Brushes.Orange);
                int count = 0;

                foreach (var lot in lots)
                {
                    bool ok = await _core.DeleteLotAsync(lot.OfferId);
                    if (ok)
                    {
                        count++;
                        AppendPluginLog($"[DEL] Удален: {Truncate(lot.Title, 30)}");
                    }
                    else
                    {
                        AppendPluginLog($"[ERR] Ошибка: {lot.OfferId}");
                    }
                    await Task.Delay(450);
                }

                AppendPluginLog($"[INFO] Готово. Удалено: {count}");
                UpdateStatus("Готово", Brushes.LightGreen);
                
                if (NodeCombo.SelectedItem != null) NodeCombo_SelectionChanged(null, null);
            }
            catch (Exception ex)
            {
                AppendPluginLog($"[ERR] {ex.Message}");
            }
            finally
            {
                NodeCombo.IsEnabled = true;
                if (_allLotsInNode.Count > 0) ChbSingleMode.IsEnabled = true;
                SearchInput.IsEnabled = true;
                CategoryCombo.IsEnabled = true;
            }
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        // --- Вспомогательные методы ---
        private Task<bool> ShowConfirmOverlay(string msg)
        {
            _confirmTcs = new TaskCompletionSource<bool>();
            if (ConfirmationOverlay != null && ConfirmMessage != null)
            {
                ConfirmMessage.Text = msg;
                ConfirmationOverlay.Visibility = Visibility.Visible;
            }
            return _confirmTcs.Task;
        }

        private void OnOverlayConfirmClick(object sender, RoutedEventArgs e)
        {
            ConfirmationOverlay.Visibility = Visibility.Collapsed;
            _confirmTcs?.TrySetResult(true);
        }

        private void OnOverlayCancelClick(object sender, RoutedEventArgs e)
        {
            ConfirmationOverlay.Visibility = Visibility.Collapsed;
            _confirmTcs?.TrySetResult(false);
        }
        
        private void OnOverlayBackgroundClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConfirmationOverlay.Visibility = Visibility.Collapsed;
            _confirmTcs?.TrySetResult(false);
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e) => AppendPluginLog("[CLR] Лог очищен");

        private void UpdateStatus(string text, Brush color)
        {
            if (PluginStatus != null)
            {
                PluginStatus.Text = text;
                PluginStatus.Foreground = color;
            }
        }

        private void AppendPluginLog(string msg)
        {
            if (_sharedLog == null) return;
            Brush color = Brushes.White;
            try { if (Application.Current.Resources["BrushText"] is SolidColorBrush b) color = b; } catch {}

            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[DEL]")) color = Brushes.OrangeRed;
            else if (msg.Contains("[INFO]")) color = Brushes.LightGreen;

            Application.Current.Dispatcher.Invoke(() => 
            {
                _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = msg, Color = color });
            });
        }
    }
}