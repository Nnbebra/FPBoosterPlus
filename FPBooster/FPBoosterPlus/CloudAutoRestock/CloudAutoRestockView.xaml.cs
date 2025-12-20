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

// ПСЕВДОНИМЫ
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

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

        // ОБЫЧНЫЙ КОНСТРУКТОР (БЕЗ АРГУМЕНТОВ)
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

        // Метод для подключения общего лога
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
                
                Log("Данные импортированы из лаунчера", Brushes.Cyan);
            } 
            catch { Log("Ошибка импорта", Brushes.Red); }
        }

        private async void OnLoadOffersClick(object sender, RoutedEventArgs e)
        {
            var key = InputKey.Password;
            var nodes = InputNodes.Text.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).ToList();

            if (string.IsNullOrEmpty(key) || !nodes.Any()) 
            {
                Log("Введите Golden Key и Node ID!", Brushes.Orange);
                return;
            }

            BtnLoadOffers.IsEnabled = false;
            Log("Анализ офферов (это может занять время)...", Brushes.Gray);

            try
            {
                var result = await CloudApiClient.Instance.FetchRestockOffersAsync(key, nodes);
                
                if (result != null && result.Success)
                {
                    int added = 0;
                    foreach (var fetched in result.Data)
                    {
                        if (fetched.Valid)
                        {
                            if (!_offers.Any(x => x.OfferId == fetched.OfferId))
                            {
                                _offers.Add(new OfferViewModel
                                {
                                    NodeId = fetched.NodeId,
                                    OfferId = fetched.OfferId,
                                    Name = fetched.Name,
                                    MinQty = 5,
                                    StatusInfo = "Новый"
                                });
                                added++;
                            }
                        }
                        else
                        {
                            // Ошибки по конкретным нодам не выводим в основной лог, чтобы не спамить
                        }
                    }
                    Log($"Загружено {added} офферов", Brushes.LightGreen);
                }
                else
                {
                    Log($"Ошибка сервера: {result?.Message}", Brushes.Red);
                }
            }
            catch (Exception ex)
            {
                Log($"Исключение: {ex.Message}", Brushes.Red);
            }
            finally
            {
                BtnLoadOffers.IsEnabled = true;
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            BtnSave.IsEnabled = false;
            Log("Сохранение...", Brushes.Gray);

            var apiList = new List<CloudApiClient.LotRestockConfig>();
            
            foreach (var vm in _offers)
            {
                var keys = vm.KeysToAddRaw.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).Select(k=>k.Trim()).ToList();
                
                apiList.Add(new CloudApiClient.LotRestockConfig
                {
                    NodeId = vm.NodeId,
                    OfferId = vm.OfferId,
                    Name = vm.Name,
                    MinQty = vm.MinQty,
                    AddSecrets = keys
                });
            }

            var res = await CloudApiClient.Instance.SaveAutoRestockAsync(
                InputKey.Password, 
                SwitchActive.IsChecked == true, 
                apiList
            );

            if (res.Success)
            {
                Log("✅ Конфигурация сохранена", Brushes.LightGreen);
                foreach (var vm in _offers) vm.KeysToAddRaw = "";
                // Обновляем view, чтобы стереть введенные ключи
                var temp = _offers.ToList(); _offers.Clear(); foreach(var t in temp) _offers.Add(t);
                
                await SyncWithServer(); 
            }
            else
            {
                Log($"Ошибка: {res.Message}", Brushes.Red);
            }

            BtnSave.IsEnabled = true;
        }

        private async Task SyncWithServer()
        {
            var status = await CloudApiClient.Instance.GetAutoRestockStatusAsync();
            if (status != null)
            {
                SwitchActive.IsChecked = status.Active;
                TxtStatus.Text = status.Message;

                foreach (var sLot in status.Lots)
                {
                    var existing = _offers.FirstOrDefault(x => x.OfferId == sLot.OfferId);
                    if (existing != null)
                    {
                        existing.StatusInfo = $"Ключей на сервере: {sLot.KeysInDb}";
                        existing.MinQty = sLot.MinQty;
                    }
                    else
                    {
                        _offers.Add(new OfferViewModel
                        {
                            NodeId = sLot.NodeId,
                            OfferId = sLot.OfferId,
                            Name = sLot.Name,
                            MinQty = sLot.MinQty,
                            StatusInfo = $"Ключей на сервере: {sLot.KeysInDb}"
                        });
                    }
                }
            }
        }

        private void OnDeleteOfferClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is OfferViewModel vm)
            {
                _offers.Remove(vm);
            }
        }

        private void LoadLocalConfig() { try { var c = ConfigManager.Load(); InputKey.Password = c.GoldenKey; } catch { } }
        private void Log(string m, Brush c) => Logs.Insert(0, new FPBooster.MainWindow.LogEntry { Text = $"[{DateTime.Now:HH:mm}] {m}", Color = c });
        private void OnClearLogClick(object s, RoutedEventArgs e) => Logs.Clear();
        private void OnBackClick(object s, RoutedEventArgs e) => NavigateBack?.Invoke();
    }
}