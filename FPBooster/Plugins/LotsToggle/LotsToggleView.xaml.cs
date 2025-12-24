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
    // Класс для UI категорий (совпадает с LotsDelete)
    public class ToggleUiNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name; 
    }

    public partial class LotsToggleView : UserControl, IPlugin
    {
        private readonly LotsToggleCore _core;
        private string _goldenKey = "";
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        
        private List<string> _rawNodeIds = new();
        private ObservableCollection<ToggleUiNode> _uiNodes = new(); 
        
        // Список лотов текущей категории
        private List<ToggleLotInfo> _allLotsInNode = new();

        public string Id => "lots_toggle";
        public string DisplayName => "Включение/Выключение";
        public UserControl GetView() => this;

        public LotsToggleView()
        {
            InitializeComponent();
            _core = new LotsToggleCore(new HttpClient());

            // Сброс при уходе
            this.Unloaded += (s, e) => ResetState();
        }

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            ResetState();

            _goldenKey = goldenKey;
            _rawNodeIds = nodes.ToList();

            if (!string.IsNullOrEmpty(_goldenKey))
            {
                var client = LotsToggleCore.CreateClientWithCookie(_goldenKey);
                _core.SetHttpClient(client);
                
                Dispatcher.Invoke(() => 
                {
                    NodeCombo.ItemsSource = _uiNodes;
                    NodeCombo.DisplayMemberPath = "Name";
                    
                    _uiNodes.Clear();
                    foreach(var id in _rawNodeIds)
                    {
                        _uiNodes.Add(new ToggleUiNode { Id = id, Name = $"Раздел {id}" });
                    }
                });

                LoadNodeNamesAsync();
            }
        }

        private void ResetState()
        {
            NodeCombo.SelectedItem = null;
            NodeCombo.SelectedIndex = -1;
            
            _allLotsInNode.Clear();
            OfferCombo.ItemsSource = null;
            OfferCombo.SelectedItem = null;

            SearchInput.Text = "";
            SpecificLotCheck.IsChecked = false;
            SpecificLotCheck.IsEnabled = false;

            if (OfferSelectPanel != null) OfferSelectPanel.Visibility = Visibility.Collapsed;

            if (BtnActivate != null) BtnActivate.IsEnabled = false;
            if (BtnDeactivate != null) BtnDeactivate.IsEnabled = false;

            if (PluginStatus != null)
            {
                PluginStatus.Text = "READY";
                try {
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

        // --- UI LOGIC ---

        private async void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodeCombo.SelectedItem is ToggleUiNode node)
            {
                // Сброс полей лотов
                OfferCombo.ItemsSource = null;
                SearchInput.Text = "";
                SpecificLotCheck.IsChecked = false;
                SpecificLotCheck.IsEnabled = false;
                OfferSelectPanel.Visibility = Visibility.Collapsed;
                
                BtnActivate.IsEnabled = false;
                BtnDeactivate.IsEnabled = false;

                UpdateStatus($"Загрузка: {Truncate(node.Name, 20)}...", Brushes.Yellow);

                try
                {
                    var lots = await _core.GetLotsFromNode(node.Id);
                    
                    // Обрезаем названия для отображения
                    foreach(var lot in lots)
                    {
                        lot.Title = Truncate(lot.FullTitle, 55);
                    }

                    _allLotsInNode = lots;
                    OfferCombo.ItemsSource = _allLotsInNode;

                    if (lots.Count > 0)
                    {
                        SpecificLotCheck.IsEnabled = true;
                        BtnActivate.IsEnabled = true;
                        BtnDeactivate.IsEnabled = true;
                        UpdateStatus($"Найдено: {lots.Count}", Brushes.LightGreen);
                    }
                    else
                    {
                        UpdateStatus("Нет лотов", Brushes.White);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[ERR] {ex.Message}");
                    UpdateStatus("Ошибка", Brushes.IndianRed);
                }
            }
        }

        private void SpecificLotCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = SpecificLotCheck.IsChecked == true;
            OfferSelectPanel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;

            if (!isChecked)
            {
                OfferCombo.SelectedItem = null;
                SearchInput.Text = "";
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (OfferCombo.ItemsSource == null) return;
            string filterText = SearchInput.Text;
            
            ICollectionView view = CollectionViewSource.GetDefaultView(OfferCombo.ItemsSource);
            if (view == null) return;

            view.Filter = (obj) =>
            {
                if (string.IsNullOrEmpty(filterText)) return true;
                if (obj is ToggleLotInfo lot)
                {
                    return lot.FullTitle.Contains(filterText, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            };
            view.Refresh();
            
            if (!string.IsNullOrEmpty(filterText))
            {
                OfferCombo.IsDropDownOpen = true;
            }
        }

        // --- ДЕЙСТВИЯ (Вкл / Выкл) ---

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            await RunToggleProcess(true);
        }

        private async void BtnDeactivate_Click(object sender, RoutedEventArgs e)
        {
            await RunToggleProcess(false);
        }

        private async Task RunToggleProcess(bool enable)
        {
            List<ToggleLotInfo> targets = new();

            // 1. Если выбрана галочка и конкретный лот -> работаем с одним
            if (SpecificLotCheck.IsChecked == true && OfferCombo.SelectedItem is ToggleLotInfo singleLot)
            {
                targets.Add(singleLot);
            }
            // 2. Если галочка НЕ выбрана и выбран раздел -> работаем со всеми в разделе
            else if (SpecificLotCheck.IsChecked == false && _allLotsInNode.Count > 0)
            {
                targets = _allLotsInNode;
            }
            // 3. Если раздел не выбран (и галочка не стоит) -> Глобально
            else if (NodeCombo.SelectedIndex == -1 && _rawNodeIds.Count > 0)
            {
                // Глобальный режим
                await RunGlobalProcess(enable);
                return;
            }

            if (targets.Count == 0)
            {
                AppendLog("[WARN] Нет лотов для обработки");
                return;
            }

            BlockUi(true);
            try
            {
                string actionName = enable ? "Включение" : "Выключение";
                UpdateStatus($"{actionName}...", Brushes.Orange);
                
                int count = 0;
                foreach (var lot in targets)
                {
                    bool ok = await _core.ToggleLotAsync(lot.OfferId, enable);
                    if (ok)
                    {
                        count++;
                        string icon = enable ? "✅" : "⛔";
                        AppendLog($"[{icon}] {actionName}: {Truncate(lot.Title, 30)}");
                    }
                    else
                    {
                        AppendLog($"[ERR] Ошибка {lot.OfferId}");
                    }
                    await Task.Delay(450); // Задержка между запросами
                }
                
                UpdateStatus("Готово", Brushes.LightGreen);
                AppendLog($"[INFO] Завершено. Успешно: {count}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] {ex.Message}");
            }
            finally
            {
                BlockUi(false);
            }
        }

        private async Task RunGlobalProcess(bool enable)
        {
             BlockUi(true);
             try
             {
                string actionName = enable ? "Включение" : "Выключение";
                int total = 0;

                foreach(var nodeId in _rawNodeIds)
                {
                    UpdateStatus($"Сканирование {nodeId}...", Brushes.Orange);
                    var lots = await _core.GetLotsFromNode(nodeId);
                    
                    if (lots.Count > 0)
                    {
                        foreach(var lot in lots)
                        {
                            bool ok = await _core.ToggleLotAsync(lot.OfferId, enable);
                            if (ok)
                            {
                                total++;
                                string icon = enable ? "✅" : "⛔";
                                AppendLog($"[{icon}] {Truncate(lot.FullTitle, 30)}");
                            }
                            await Task.Delay(450);
                        }
                    }
                    await Task.Delay(500);
                }
                UpdateStatus("Готово", Brushes.LightGreen);
                AppendLog($"[FINISH] Глобально обработано: {total}");
             }
             catch(Exception ex)
             {
                 AppendLog($"[ERR] {ex.Message}");
             }
             finally
             {
                 BlockUi(false);
                 ResetState(); // Полный сброс после глобальной операции
                 NodeCombo.IsEnabled = true; // Разрешаем выбор
             }
        }

        private void BlockUi(bool block)
        {
            NodeCombo.IsEnabled = !block;
            SpecificLotCheck.IsEnabled = !block && _allLotsInNode.Count > 0;
            SearchInput.IsEnabled = !block;
            OfferCombo.IsEnabled = !block;
            BtnActivate.IsEnabled = !block;
            BtnDeactivate.IsEnabled = !block;
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private void OnClearLog_Click(object sender, RoutedEventArgs e) => AppendLog("[CLR] Лог очищен");

        private void UpdateStatus(string text, Brush color)
        {
            if (PluginStatus != null)
            {
                PluginStatus.Text = text;
                PluginStatus.Foreground = color;
            }
        }

        private void AppendLog(string msg)
        {
            if (_sharedLog == null) return;
            Brush color = Brushes.White;
            try { if (Application.Current.Resources["BrushText"] is SolidColorBrush b) color = b; } catch {}

            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("✅")) color = Brushes.LightGreen;
            else if (msg.Contains("⛔")) color = Brushes.Orange;

            Application.Current.Dispatcher.Invoke(() => 
            {
                _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = msg, Color = color });
            });
        }
    }
}