using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading.Tasks;

// Псевдонимы
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;

namespace FPBooster.Plugins
{
    public partial class AutoRestockView : UserControl, IPlugin
    {
        private readonly AutoRestockCore _core;
        private string _goldenKey = "";
        private DispatcherTimer _statusTimer;
        private bool _isInitialized = false;
        private bool _isLoadingOffers = false;
        
        private Dictionary<string, (string Text, string Qty)> _offerDataCache = new();

        public string Id => "auto_restock";
        public string DisplayName => "Автопополнение";

        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;

        public AutoRestockView()
        {
            InitializeComponent();
            _core = new AutoRestockCore(new HttpClient());
            
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => UpdateStatus();
            _statusTimer.Start();

            this.Loaded += (s, e) => 
            {
                if (!_isInitialized)
                {
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
                    this.BeginAnimation(OpacityProperty, fadeIn);
                    _isInitialized = true;
                }
                UpdatePreview();
            };
        }

        public UserControl GetView() => this;
        
        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
            PluginLog.ItemsSource = _sharedLog;
            if (_sharedLog != null)
            {
                _sharedLog.CollectionChanged += (s, e) => {
                    if (PluginLog.Items.Count > 0)
                        PluginLog.ScrollIntoView(PluginLog.Items[PluginLog.Items.Count - 1]);
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
                AppendPluginLog($"[WARN] Ошибка InitNodes: {ex.Message}");
            }

            OfferCombo.ItemsSource = null;
            _offerDataCache.Clear();

            var nodeList = nodes?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            var currentSelection = NodeCombo.SelectedItem as string;

            NodeCombo.SelectionChanged -= NodeCombo_SelectionChanged;
            NodeCombo.ItemsSource = nodeList;
            
            if (nodeList.Count > 0)
            {
                if (!string.IsNullOrEmpty(currentSelection) && nodeList.Contains(currentSelection))
                    NodeCombo.SelectedItem = currentSelection;
                else
                    NodeCombo.SelectedIndex = 0;
            }
            else
            {
                NodeCombo.SelectedItem = null;
            }
            
            NodeCombo.SelectionChanged += NodeCombo_SelectionChanged;
            UpdateStatus($"Загружено {nodeList.Count} разделов");

            if (NodeCombo.SelectedItem != null)
            {
                _ = LoadOffersAsync(NodeCombo.SelectedItem.ToString());
            }
        }

        private void UpdateStatus(string? status = null)
        {
            if (status != null) { PluginStatus.Text = status; return; }
            if (string.IsNullOrEmpty(_goldenKey)) PluginStatus.Text = "Нет GoldenKey";
            else if (_isLoadingOffers) PluginStatus.Text = "Загрузка...";
            else PluginStatus.Text = "Готов";
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e)
        {
            _sharedLog?.Clear();
            AppendPluginLog("[CLR] Журнал очищен");
        }

        private async void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var node = NodeCombo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(node)) { OfferCombo.ItemsSource = null; return; }
            await LoadOffersAsync(node);
        }

        private void OfferCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OfferCombo.SelectedItem is LotInfo lot)
            {
                if (_offerDataCache.ContainsKey(lot.OfferId)) {
                    var data = _offerDataCache[lot.OfferId];
                    ItemInput.Text = data.Text;
                    QtyInput.Text = data.Qty;
                } else {
                    ItemInput.Text = "";
                    QtyInput.Text = "1";
                }
            }
        }

        private async Task LoadOffersAsync(string node)
        {
            if (_isLoadingOffers) return;
            _isLoadingOffers = true;
            try {
                OfferCombo.SelectionChanged -= OfferCombo_SelectionChanged;
                OfferCombo.ItemsSource = null;
                OffersLoadingIndicator.Visibility = Visibility.Visible;
                UpdateStatus("Загрузка офферов...");

                var lots = await _core.FetchOffersByNodeAsync(node);
                if (lots == null || lots.Count == 0) ShowToast("Офферы не найдены");
                else {
                    OfferCombo.ItemsSource = lots;
                    OfferCombo.DisplayMemberPath = "Title"; 
                    OfferCombo.SelectionChanged += OfferCombo_SelectionChanged;
                    if (lots.Count > 0) OfferCombo.SelectedIndex = 0;
                    AppendPluginLog($"[INFO] Загружено офферов: {lots.Count}");
                }
            }
            catch (Exception ex) { AppendPluginLog($"[ERR] Ошибка загрузки: {ex.Message}"); }
            finally {
                _isLoadingOffers = false;
                OffersLoadingIndicator.Visibility = Visibility.Collapsed;
                UpdateStatus();
            }
        }

        private void ItemInput_TextChanged(object sender, TextChangedEventArgs e) { SaveToCache(); UpdatePreview(); }
        private void QtyInput_TextChanged(object sender, TextChangedEventArgs e) { 
            if (!int.TryParse(QtyInput.Text, out _)) if (!string.IsNullOrEmpty(QtyInput.Text)) QtyInput.Text = "1";
            SaveToCache(); UpdatePreview(); 
        }
        private void SaveToCache() { if (OfferCombo.SelectedItem is LotInfo lot) _offerDataCache[lot.OfferId] = (ItemInput.Text, QtyInput.Text); }
        private void BtnMinus_Click(object sender, RoutedEventArgs e) { if (int.TryParse(QtyInput.Text, out int val) && val > 1) QtyInput.Text = (val - 1).ToString(); else QtyInput.Text = "1"; }
        private void BtnPlus_Click(object sender, RoutedEventArgs e) { if (int.TryParse(QtyInput.Text, out int val)) QtyInput.Text = (val + 1).ToString(); else QtyInput.Text = "1"; }

        private void UpdatePreview()
        {
            if (PreviewText == null) return;
            string itemText = ItemInput?.Text?.Trim() ?? "";
            string qtyText = QtyInput?.Text?.Trim() ?? "1";
            if (string.IsNullOrEmpty(itemText)) { PreviewText.Text = "..."; return; }
            if (int.TryParse(qtyText, out int quantity) && quantity > 1) PreviewText.Text = $"{itemText}\n[x{quantity} шт.]";
            else PreviewText.Text = itemText;
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var lot = OfferCombo.SelectedItem as LotInfo;
            var nodeId = NodeCombo.SelectedItem?.ToString();
            if (lot == null || string.IsNullOrEmpty(nodeId)) { ShowToast("Выберите раздел и оффер!"); return; }
            string itemText = ItemInput?.Text ?? "";
            if (string.IsNullOrWhiteSpace(itemText)) { ShowToast("Введите текст выдачи!"); return; }
            if (!int.TryParse(QtyInput?.Text, out int amount) || amount <= 0) amount = 1;
            bool autoDelivery = AutoDeliveryCheck.IsChecked == true;
            bool autoActivate = AutoActivateCheck.IsChecked == true;

            if (sender is Button btn) {
                btn.IsEnabled = false;
                btn.Content = "⏳ Сохранение...";
                try {
                    AppendPluginLog($"[INFO] Сохранение лота {lot.OfferId}...");
                    bool ok = await _core.RestockLotAsync(_goldenKey, nodeId, lot.OfferId, itemText, amount, autoDelivery, autoActivate);
                    if (ok) { AppendPluginLog($"[SUCCESS] Лот успешно обновлён!"); ShowToast("Сохранено!", true); }
                    else { AppendPluginLog($"[ERR] Не удалось сохранить лот."); ShowToast("Ошибка FunPay"); }
                }
                catch (Exception ex) { AppendPluginLog($"[ERR] Исключение: {ex.Message}"); }
                finally { btn.IsEnabled = true; btn.Content = "💾 Сохранить настройки"; }
            }
        }

        private void ShowToast(string message, bool success = false)
        {
            PluginStatus.Text = message;
            PluginStatus.Foreground = success ? Brushes.LightGreen : Brushes.Orange;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => { UpdateStatus(); timer.Stop(); };
            timer.Start();
        }

        private void AppendPluginLog(string msg)
        {
            if (_sharedLog == null) return;
            Brush color = Brushes.White;
            if (Application.Current.Resources["BrushText"] is SolidColorBrush themeBrush) color = themeBrush;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[WARN]")) color = Brushes.Orange;
            else if (msg.Contains("[SUCCESS]")) color = Brushes.LightGreen;
            else if (msg.Contains("[THEME]")) color = Brushes.HotPink;
            _sharedLog.Add(new FPBooster.MainWindow.LogEntry { Text = msg, Color = color });
        }
    }
}