using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FPBooster.ServerApi;
using FPBooster.Config;

// ЯВНЫЕ ПСЕВДОНИМЫ ДЛЯ WPF (чтобы не путать с WinForms)
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudAutoRestockView : UserControl
    {
        public event Action NavigateBack;
        
        private List<CloudApiClient.LotRestockConfig> _lots = new List<CloudApiClient.LotRestockConfig>();

        public class LotViewModel : CloudApiClient.LotRestockConfig 
        {
            public string KeysInfo { get; set; } = "Ключей в буфере: 0";
        }

        public CloudAutoRestockView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadData();
        }

        private async System.Threading.Tasks.Task LoadData()
        {
            var status = await CloudApiClient.Instance.GetAutoRestockStatusAsync();
            if (status != null)
            {
                SwitchActive.IsChecked = status.Active;
                TxtStatus.Text = "Статус: " + status.Message;
                
                _lots.Clear();
                var viewList = new List<LotViewModel>();
                
                foreach(var l in status.Lots)
                {
                    var conf = new CloudApiClient.LotRestockConfig { NodeId = l.NodeId, MinQty = 0 }; 
                    _lots.Add(conf);
                    viewList.Add(new LotViewModel { NodeId = l.NodeId, KeysInfo = $"Ключей на сервере: {l.KeysInDb}" });
                }
                ListLots.ItemsSource = viewList;
            }
        }

        private void OnAddLotClick(object sender, RoutedEventArgs e)
        {
            string nid = InputNodeId.Text.Trim();
            if (!int.TryParse(InputLimit.Text, out int limit)) limit = 5;
            var keys = InputKeys.Text.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries).ToList();

            if (string.IsNullOrEmpty(nid)) return;

            var newItem = new CloudApiClient.LotRestockConfig { NodeId = nid, MinQty = limit, AddSecrets = keys };
            
            var existing = _lots.FirstOrDefault(x => x.NodeId == nid);
            if (existing != null) _lots.Remove(existing);
            _lots.Add(newItem);

            RefreshList();
            InputKeys.Clear();
            InputNodeId.Clear();
        }

        private void RefreshList()
        {
            var view = _lots.Select(x => new LotViewModel 
            { 
                NodeId = x.NodeId, 
                MinQty = x.MinQty, 
                KeysInfo = x.AddSecrets.Count > 0 ? $"Будет добавлено ключей: {x.AddSecrets.Count}" : "Без добавления ключей (обновление настроек)"
            }).ToList();
            ListLots.ItemsSource = view;
        }

        private void OnDeleteLotClick(object sender, RoutedEventArgs e)
        {
            // Явное приведение к WPF Button
            if ((sender as Button)?.DataContext is LotViewModel vm)
            {
                var item = _lots.FirstOrDefault(x => x.NodeId == vm.NodeId);
                if (item != null) _lots.Remove(item);
                RefreshList();
            }
        }

        private async void OnSaveServerClick(object sender, RoutedEventArgs e)
        {
            BtnSaveServer.IsEnabled = false;
            var cfg = ConfigManager.Load();
            
            var res = await CloudApiClient.Instance.SaveAutoRestockAsync(cfg.GoldenKey, SwitchActive.IsChecked == true, _lots);
            
            if (res.Success) MessageBox.Show("Сохранено! Ключи отправлены на сервер.");
            else MessageBox.Show("Ошибка: " + res.Message);
            
            foreach(var l in _lots) l.AddSecrets.Clear();
            RefreshList();
            
            BtnSaveServer.IsEnabled = true;
        }

        private void OnBackClick(object sender, RoutedEventArgs e) => NavigateBack?.Invoke();
    }
}