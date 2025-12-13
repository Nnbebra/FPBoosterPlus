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
    public partial class LotsDeleteView : UserControl, IPlugin
    {
        private readonly LotsDeleteCore _core;
        private string _goldenKey = "";
        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private List<string> _allNodeIds = new();
        private ObservableCollection<LotInfo> _currentLots = new();
        
        // Для ожидания ответа от красивого диалога
        private TaskCompletionSource<bool>? _confirmTcs;

        public string Id => "lots_delete";
        public string DisplayName => "Удаление лотов";
        
        public LotsDeleteView()
        {
            InitializeComponent();
            _core = new LotsDeleteCore(new HttpClient());
            
            this.Loaded += (s, e) => AnimateEntrance();
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
            PluginLog.ItemsSource = _sharedLog;
        }

        public void SetTheme(string themeKey) { }

        public async void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey ?? "";
            
            try
            {
                var client = LotsDeleteCore.CreateClientWithCookie(_goldenKey);
                _core.SetHttpClient(client);
                UpdateStatus("CLIENT READY");
            }
            catch (Exception ex)
            {
                AppendPluginLog($"[WARN] Client Init Error: {ex.Message}");
                UpdateStatus("CONN ERROR");
                return;
            }

            _allNodeIds = nodes?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();

            try
            {
                UpdateStatus("SCANNING...");
                var profileNodes = await _core.GetActiveNodeIdsFromProfileAsync();
                
                int newNodes = 0;
                foreach (var node in profileNodes)
                {
                    if (!_allNodeIds.Contains(node))
                    {
                        _allNodeIds.Add(node);
                        newNodes++;
                    }
                }
                if (newNodes > 0) AppendPluginLog($"[INFO] New categories found: {newNodes}");
            }
            catch (Exception ex)
            {
                AppendPluginLog($"[WARN] Profile scan error: {ex.Message}");
            }

            NodeCombo.ItemsSource = null;
            NodeCombo.ItemsSource = _allNodeIds;
            if (NodeCombo.Items.Count > 0)
            {
                NodeCombo.SelectedIndex = 0;
                UpdateStatus("READY");
            }
            else
            {
                UpdateStatus("NO NODES");
                AppendPluginLog("[WARN] No NodeIDs found.");
            }
        }

        private async void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var nodeId = NodeCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(nodeId)) return;

            CategoryCombo.ItemsSource = null;
            BtnDeleteCategory.IsEnabled = false;
            
            UpdateStatus("LOADING...");
            try 
            {
                var fetchedLots = await _core.FetchOffersByNodeAsync(nodeId);
                _currentLots = new ObservableCollection<LotInfo>(fetchedLots);
                int count = _currentLots.Count;
                
                AppendPluginLog($"[INFO] Node {nodeId}: found {count} lots");
                if (count > 0)
                {
                    UpdateStatus($"{count} LOTS");
                    CategoryCombo.ItemsSource = _currentLots;
                    CategoryCombo.IsEnabled = true;
                    CategoryCombo.SelectedIndex = 0;
                    
                    BtnDeleteCategory.IsEnabled = true;
                }
                else
                {
                    UpdateStatus("EMPTY");
                    CategoryCombo.IsEnabled = false;
                    BtnDeleteCategory.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                AppendPluginLog($"[ERR] Scan error: {ex.Message}");
                UpdateStatus("ERROR");
            }
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
            _sharedLog?.Clear();
            AppendPluginLog("[CLR] Log cleared");
        }

        // === ЛОГИКА КРАСИВОГО ОКНА (OVERLAY) ===

        private async Task<bool> ShowConfirmAsync(string title, string message)
        {
            ConfirmTitle.Text = title;
            ConfirmMessage.Text = message;
            
            // Показываем оверлей с анимацией
            ConfirmationOverlay.Opacity = 0;
            ConfirmationOverlay.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            ConfirmationOverlay.BeginAnimation(OpacityProperty, fadeIn);

            _confirmTcs = new TaskCompletionSource<bool>();
            return await _confirmTcs.Task;
        }

        private void HideOverlay()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) => ConfirmationOverlay.Visibility = Visibility.Collapsed;
            ConfirmationOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void OnOverlayConfirmClick(object sender, RoutedEventArgs e)
        {
            _confirmTcs?.TrySetResult(true);
            HideOverlay();
        }

        private void OnOverlayCancelClick(object sender, RoutedEventArgs e)
        {
            OnOverlayBackgroundClick(null, null);
        }

        private void OnOverlayBackgroundClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _confirmTcs?.TrySetResult(false);
            HideOverlay();
        }

        // ========================================

        private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
        {
            var selectedLot = CategoryCombo.SelectedItem as LotInfo;
            if (selectedLot == null)
            {
                ShowToast("Please select a lot first");
                return;
            }

            // Вызов красивого окна
            bool confirm = await ShowConfirmAsync(
                "УДАЛЕНИЕ ЛОТА", 
                $"Вы действительно хотите удалить лот:\n«{selectedLot.Title}»?"
            );

            if (!confirm) return;

            SetButtonsEnabled(false);
            UpdateStatus("DELETING...");
            
            try
            {
                bool ok = await _core.DeleteLotAsync(selectedLot.NodeId, selectedLot.OfferId);
                if (ok)
                {
                    AppendPluginLog($"[DEL] Deleted: '{selectedLot.Title}'");
                    ShowToast("Lot deleted", true);
                    
                    _currentLots.Remove(selectedLot);
                    
                    if (_currentLots.Count == 0)
                    {
                        UpdateStatus("EMPTY");
                        BtnDeleteCategory.IsEnabled = false;
                        CategoryCombo.IsEnabled = false;
                    }
                    else
                    {
                        CategoryCombo.SelectedIndex = 0;
                        UpdateStatus($"{_currentLots.Count} LOTS LEFT");
                    }
                }
                else
                {
                    AppendPluginLog($"[ERR] Failed to delete {selectedLot.OfferId}");
                    UpdateStatus("ERROR");
                }
            }
            catch (Exception ex)
            {
                AppendPluginLog($"[ERR] Error: {ex.Message}");
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private async void OnDeleteAllClick(object sender, RoutedEventArgs e)
        {
            var nodeId = NodeCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(nodeId))
            {
                ShowToast("Select a node first!");
                return;
            }

            if (_currentLots == null || _currentLots.Count == 0)
            {
                var fetched = await _core.FetchOffersByNodeAsync(nodeId);
                _currentLots = new ObservableCollection<LotInfo>(fetched);
            }

            if (_currentLots.Count == 0)
            {
                ShowToast("No lots to delete.");
                return;
            }

            // Вызов красивого окна (массовое удаление)
            bool confirm = await ShowConfirmAsync(
                "МАССОВОЕ УДАЛЕНИЕ", 
                $"ВНИМАНИЕ! Вы собираетесь удалить ВСЕ ({_currentLots.Count}) лоты в разделе {nodeId}.\n\nЭто действие нельзя отменить. Продолжить?"
            );

            if (!confirm) return;

            SetButtonsEnabled(false);
            UpdateStatus("MASS DELETING...");
            AppendPluginLog($"[INFO] Starting mass delete of {_currentLots.Count} lots...");
            try
            {
                var lotsList = _currentLots.ToList();
                int count = await _core.DeleteLotsListAsync(lotsList, (msg) => AppendPluginLog(msg));
                
                AppendPluginLog($"[INFO] Finished. Deleted: {count} / {lotsList.Count}");
                ShowToast($"Deleted {count} lots", true);
                UpdateStatus("DONE");
                
                _currentLots.Clear();
                CategoryCombo.ItemsSource = null;
                BtnDeleteCategory.IsEnabled = false;
                CategoryCombo.IsEnabled = false;
            }
            catch (Exception ex)
            {
                AppendPluginLog($"[ERR] Critical Error: {ex.Message}");
                UpdateStatus("FAILED");
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool val)
        {
            BtnDeleteAll.IsEnabled = val;
            if (BtnDeleteCategory != null) BtnDeleteCategory.IsEnabled = val && _currentLots.Count > 0;
            if (CategoryCombo != null) CategoryCombo.IsEnabled = val && _currentLots.Count > 0;
            NodeCombo.IsEnabled = val;
        }

        private void UpdateStatus(string status)
        {
            if (PluginStatus == null) return;
            PluginStatus.Text = status;
            
            if (status.Contains("DELETING") || status.Contains("SCANNING") || status.Contains("LOADING")) 
                PluginStatus.Foreground = Brushes.Yellow;
            else if (status.Contains("READY") || status.Contains("DONE") || status.Contains("LOTS")) 
                PluginStatus.Foreground = Brushes.LightGreen;
            else if (status.Contains("ERROR") || status.Contains("FAILED") || status.Contains("NO")) 
                PluginStatus.Foreground = Brushes.IndianRed;
            else 
                PluginStatus.Foreground = Brushes.White;
        }

        private void ShowToast(string msg, bool success = false) 
        {
            AppendPluginLog($"[{(success ? "INFO" : "WARN")}] {msg}");
        }

        private void AppendPluginLog(string msg)
        {
            if (_sharedLog == null) return;
            
            Brush color = Brushes.White;
            if (Application.Current.Resources["BrushText"] is SolidColorBrush themeBrush)
                color = themeBrush;

            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[WARN]")) color = Brushes.Goldenrod;
            else if (msg.Contains("[DEL]")) color = Brushes.OrangeRed;
            else if (msg.Contains("[INFO]")) color = Brushes.LightGreen;
            else if (msg.Contains("[CLR]")) color = Brushes.Gray;

            _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = msg, Color = color });
            try 
            {
                if (PluginLog != null && PluginLog.Items.Count > 0)
                    PluginLog.ScrollIntoView(PluginLog.Items[PluginLog.Items.Count - 1]);
            } 
            catch { }
        }
    }
}