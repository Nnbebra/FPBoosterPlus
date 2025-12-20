using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FPBooster.ServerApi;
using FPBooster.Config;

using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudAutoRestockView : UserControl
    {
        public event Action NavigateBack;
        public ObservableCollection<FPBooster.MainWindow.LogEntry> Logs { get; private set; } = new ObservableCollection<FPBooster.MainWindow.LogEntry>();
        
        public class OfferViewModel
        {
            public string NodeId { get; set; } = "";
            public string OfferId { get; set; } = "";
            public string Name { get; set; } = "";
            public int MinQty { get; set; } = 5;
            public string KeysToAddRaw { get; set; } = ""; 
            public string StatusInfo { get; set; } = "Новый"; 
        }

        private ObservableCollection<OfferViewModel> _offers = new ObservableCollection<OfferViewModel>();

        public CloudAutoRestockView()
        {
            InitializeComponent();
            LogList.ItemsSource = Logs;
            ListOffers.ItemsSource = _offers;
            
            Loaded += async (s, e) => { 
                LoadLocalConfig(); 
                await SyncWithServer(); 
            };
        }

        public void SetSharedLog(ObservableCollection<FPBooster.MainWindow.LogEntry> shared)
        {
            Logs = shared;
            LogList.ItemsSource = Logs;
        }

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
                if (fieldNodes?.GetValue(mainWindow) is ListBox lb) 
                { 
                    var items = lb.Items.Cast<object>().Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)); 
                    InputNodes.Text = string.Join("\n", items); 
                }
                Log("Restock: Данные импортированы", Brushes.Cyan);
            } 
            catch { Log("Restock: Ошибка импорта", Brushes.Red); }
        }

        private async void OnLoadOffersClick(object sender, RoutedEventArgs e)
        {
            var key = InputKey.Password;
            var nodes = InputNodes.Text.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).ToList();

            if (string.IsNullOrEmpty(key) || !nodes.Any()) {
                Log("Restock: Введите Golden Key и Node ID", Brushes.Orange);
                return;
            }

            BtnLoadOffers.IsEnabled = false;
            Log("Restock: Анализ лотов...", Brushes.Gray);

            try {
                var result = await CloudApiClient.Instance.FetchRestockOffersAsync(key, nodes);
                if (result != null && result.Success) {
                    foreach (var f in result.Data) {
                        if (f.Valid && !_offers.Any(o => o.OfferId == f.OfferId)) {
                            _offers.Add(new OfferViewModel { NodeId = f.NodeId, OfferId = f.OfferId, Name = f.Name });
                        }
                    }
                    Log($"Restock: Найдено {result.Data.Count} офферов", Brushes.LightGreen);
                } else Log("Restock: Ошибка сервера", Brushes.Red);
            } catch (Exception ex) { Log("Restock: " + ex.Message, Brushes.Red); }
            finally { BtnLoadOffers.IsEnabled = true; }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            BtnSave.IsEnabled = false;
            Log("Restock: Сохранение...", Brushes.Gray);

            var list = _offers.Select(o => new CloudApiClient.LotRestockConfig {
                NodeId = o.NodeId, OfferId = o.OfferId, Name = o.Name, MinQty = o.MinQty,
                AddSecrets = o.KeysToAddRaw.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).ToList()
            }).ToList();

            var res = await CloudApiClient.Instance.SaveAutoRestockAsync(InputKey.Password, SwitchActive.IsChecked == true, list);
            if (res.Success) {
                Log("Restock: ✅ Сохранено", Brushes.LightGreen);
                foreach (var o in _offers) o.KeysToAddRaw = "";
                await SyncWithServer();
            } else Log("Restock: ❌ Ошибка: " + res.Message, Brushes.Red);

            BtnSave.IsEnabled = true;
        }

        private async Task SyncWithServer()
        {
            var status = await CloudApiClient.Instance.GetAutoRestockStatusAsync();
            if (status != null) {
                SwitchActive.IsChecked = status.Active;
                TxtStatus.Text = status.Message;
                foreach (var s in status.Lots) {
                    var existing = _offers.FirstOrDefault(x => x.OfferId == s.OfferId);
                    if (existing != null) {
                        existing.StatusInfo = $"В базе: {s.KeysInDb} шт.";
                        existing.MinQty = s.MinQty;
                    } else {
                        _offers.Add(new OfferViewModel { NodeId = s.NodeId, OfferId = s.OfferId, Name = s.Name, MinQty = s.MinQty, StatusInfo = $"В базе: {s.KeysInDb} шт." });
                    }
                }
            }
        }

        private void OnDeleteOfferClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.DataContext is OfferViewModel vm) _offers.Remove(vm); }
        private void LoadLocalConfig() { try { var c = ConfigManager.Load(); InputKey.Password = c.GoldenKey; } catch { } }
        private void Log(string m, Brush c) => Logs.Insert(0, new FPBooster.MainWindow.LogEntry { Text = $"[{DateTime.Now:HH:mm}] {m}", Color = c });
        private void OnClearLogClick(object s, RoutedEventArgs e) => Logs.Clear();
        private void OnBackClick(object s, RoutedEventArgs e) => NavigateBack?.Invoke();
    }
}