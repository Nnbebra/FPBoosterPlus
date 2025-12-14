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
using System.Windows.Threading;
using FPBooster.Plugins;

// --- ПСЕВДОНИМЫ ---
using UserControl = System.Windows.Controls.UserControl;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
// ------------------

namespace FPBooster.Plugins
{
    public partial class LotsToggleView : UserControl, IPlugin
    {
        public string Id => "lots_toggle";
        public string DisplayName => "Включение/Выключение";

        private ObservableCollection<FPBooster.MainWindow.LogEntry>? _sharedLog;
        private string _goldenKey = "";
        private List<string> _nodes = new List<string>();
        private readonly LotsToggleCore _core;
        
        private List<ToggleLotInfo> _currentOffers = new List<ToggleLotInfo>();

        public LotsToggleView()
        {
            InitializeComponent();
            _core = new LotsToggleCore(new HttpClient());
            
            Loaded += (s, e) => {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        public UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
            _nodes = nodes.ToList();
            
            if (this.FindName("NodeCombo") is ComboBox cb)
            {
                cb.ItemsSource = _nodes;
                if (_nodes.Count > 0) cb.SelectedIndex = 0;
            }

            _core.SetHttpClient(LotsToggleCore.CreateClientWithCookie(_goldenKey));
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
            if (this.FindName("LogList") is System.Windows.Controls.ListBox lb)
                lb.ItemsSource = _sharedLog;
        }

        public void SetTheme(string themeKey) { }

        // --- ОБРАБОТЧИКИ СОБЫТИЙ ---

        private async void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.FindName("NodeCombo") is ComboBox cb && cb.SelectedItem != null)
            {
                string nodeId = cb.SelectedItem.ToString() ?? "";
                if (string.IsNullOrEmpty(nodeId)) return;

                // Переименована переменная loading -> loadingStart
                if (this.FindName("OffersLoadingIndicator") is TextBlock loadingStart) 
                    loadingStart.Visibility = Visibility.Visible;
                
                try 
                {
                    _currentOffers = await _core.FetchLotsAsync(nodeId);
                    UpdateStats(_currentOffers);
                    
                    if (this.FindName("OfferCombo") is ComboBox offerCb)
                    {
                        offerCb.ItemsSource = _currentOffers;
                        offerCb.DisplayMemberPath = "Title";
                        if (_currentOffers.Count > 0) offerCb.SelectedIndex = 0;
                    }
                }
                finally 
                {
                    // Переименована переменная loading -> loadingEnd
                    if (this.FindName("OffersLoadingIndicator") is TextBlock loadingEnd) 
                        loadingEnd.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void SpecificLotCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (this.FindName("OfferSelectPanel") is Grid panel && this.FindName("SpecificLotCheck") is CheckBox check)
            {
                panel.Visibility = check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.FindName("SearchInput") is TextBox tb && this.FindName("OfferCombo") is ComboBox cb)
            {
                var filter = tb.Text.ToLower();
                var filtered = _currentOffers.Where(o => o.Title.ToLower().Contains(filter)).ToList();
                cb.ItemsSource = filtered;
                if (filtered.Count > 0) cb.SelectedIndex = 0;
            }
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e) => await ProcessLots(true);
        private async void BtnDeactivate_Click(object sender, RoutedEventArgs e) => await ProcessLots(false);

        private void OnClearLog_Click(object sender, RoutedEventArgs e) => _sharedLog?.Clear();

        // --- ЛОГИКА ---

        private async Task ProcessLots(bool targetState)
        {
            if (string.IsNullOrEmpty(_goldenKey)) { MessageBox.Show("Нет ключа!"); return; }
            
            List<ToggleLotInfo> lotsToProcess = new List<ToggleLotInfo>();
            
            if (this.FindName("SpecificLotCheck") is CheckBox check && check.IsChecked == true)
            {
                if (this.FindName("OfferCombo") is ComboBox cb && cb.SelectedItem is ToggleLotInfo lot)
                    lotsToProcess.Add(lot);
            }
            else
            {
                lotsToProcess.AddRange(_currentOffers);
            }

            if (lotsToProcess.Count == 0) { MessageBox.Show("Нет лотов для обработки."); return; }

            SetButtonsEnabled(false);
            
            // Переименована переменная pp -> ppStart
            if (this.FindName("ProgressPanel") is StackPanel ppStart) ppStart.Visibility = Visibility.Visible;

            try
            {
                AppendLog($"[INFO] Запуск: {(targetState ? "Включение" : "Выключение")} {lotsToProcess.Count} лотов...");
                int processed = 0;

                foreach (var lot in lotsToProcess)
                {
                    bool ok = await _core.ToggleLotStateAsync(lot.NodeId, lot.OfferId, targetState);
                    if (ok) AppendLog($"[OK] {lot.Title} -> {(targetState ? "ON" : "OFF")}");
                    else AppendLog($"[ERR] Не удалось изменить {lot.Title}");

                    processed++;
                    UpdateProgress(processed, lotsToProcess.Count);
                    await Task.Delay(400); 
                }
                AppendLog("[DONE] Операция завершена.");
                
                foreach(var l in lotsToProcess) l.Active = targetState;
                UpdateStats(_currentOffers);
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] {ex.Message}");
            }
            finally
            {
                SetButtonsEnabled(true);
                // Переименована переменная pp -> ppEnd
                if (this.FindName("ProgressPanel") is StackPanel ppEnd) ppEnd.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateStats(List<ToggleLotInfo> lots)
        {
            if (this.FindName("StatActive") is TextBlock tA) tA.Text = lots.Count(l => l.Active).ToString();
            if (this.FindName("StatInactive") is TextBlock tI) tI.Text = lots.Count(l => !l.Active).ToString();
            if (this.FindName("StatTotal") is TextBlock tT) tT.Text = lots.Count.ToString();
        }

        private void UpdateProgress(int current, int total)
        {
            if (this.FindName("ProgressText") is TextBlock pt) pt.Text = $"Обработано: {current}/{total}";
        }

        private void SetButtonsEnabled(bool enabled)
        {
            if (this.FindName("BtnActivate") is Button b1) b1.IsEnabled = enabled;
            if (this.FindName("BtnDeactivate") is Button b2) b2.IsEnabled = enabled;
        }

        private void AppendLog(string msg)
        {
            if (_sharedLog == null) return;
            Brush color = Brushes.White;
            if (msg.Contains("[ERR]")) color = Brushes.IndianRed;
            else if (msg.Contains("[OK]") || msg.Contains("[DONE]")) color = Brushes.LightGreen;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _sharedLog.Insert(0, new FPBooster.MainWindow.LogEntry 
                { 
                    Text = $"[{DateTime.Now:HH:mm:ss}] {msg}", 
                    Color = color 
                });
            });
        }
    }
}