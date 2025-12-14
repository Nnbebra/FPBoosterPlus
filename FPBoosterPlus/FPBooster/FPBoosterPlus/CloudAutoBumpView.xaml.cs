using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FPBooster.ServerApi;
using FPBooster.Plugins;

// --- ПСЕВДОНИМЫ ---
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Application = System.Windows.Application; // <--- FIX CS0104
// ------------------

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudAutoBumpView : UserControl, IPlugin
    {
        public string Id => "fp_cloud_autobump";
        public string DisplayName => "Cloud AutoBump";

        private ObservableCollection<FPBooster.MainWindow.LogEntry> _logs = new ObservableCollection<FPBooster.MainWindow.LogEntry>();
        private DispatcherTimer _refreshTimer;
        
        private string _mainGoldenKey;
        private List<string> _mainNodes;

        public CloudAutoBumpView()
        {
            InitializeComponent();
            LogList.ItemsSource = _logs;
            
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += async (s, e) => await RefreshStatus();
            
            Loaded += async (s, e) => { await RefreshStatus(); _refreshTimer.Start(); };
            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        public UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _mainGoldenKey = goldenKey;
            _mainNodes = nodes.ToList();
            
            if (string.IsNullOrEmpty(InputKey.Text)) InputKey.Text = _mainGoldenKey;
            if (string.IsNullOrEmpty(InputNodes.Text)) InputNodes.Text = string.Join(", ", _mainNodes);
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog) { }
        public void SetTheme(string themeKey) { }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow main)
                FPBooster.UI.PluginsDialog.RunPlugin(main, "fp_plus_dashboard");
        }

        private async Task RefreshStatus()
        {
            try
            {
                var status = await CloudApiClient.Instance.GetAutoBumpStatusAsync();
                if (status != null)
                {
                    SwitchActive.IsChecked = status.IsActive;
                    TxtStatus.Text = status.StatusMessage;
                    if (status.NextBump.HasValue)
                        TxtNextRun.Text = (status.NextBump.Value.ToLocalTime() - DateTime.Now).TotalSeconds > 0 ? $"{status.NextBump.Value.ToLocalTime():HH:mm}" : "В работе...";
                    else TxtNextRun.Text = "—";
                }
                else TxtStatus.Text = "Нет данных";
            }
            catch (Exception ex) { Log($"Ошибка: {ex.Message}", Brushes.Red); }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            BtnSave.IsEnabled = false;
            try
            {
                string key = InputKey.Text.Trim();
                var nodes = InputNodes.Text.Split(new[] { ',', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Where(s => s.All(char.IsDigit)).ToList();
                
                var res = await CloudApiClient.Instance.SetAutoBumpAsync(key, nodes, SwitchActive.IsChecked == true);
                if (res.Success) { Log("✅ Сохранено!", Brushes.LightGreen); await RefreshStatus(); }
                else Log($"❌ Ошибка: {res.Message}", Brushes.IndianRed);
            }
            catch (Exception ex) { Log($"Error: {ex.Message}", Brushes.Red); }
            finally { BtnSave.IsEnabled = true; }
        }

        private void OnSwitchToggled(object sender, RoutedEventArgs e) => OnSaveClick(sender, e);
        
        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            Log("Обновление...", Brushes.Gray);
            await CloudApiClient.Instance.ForceCheckAutoBumpAsync();
            await Task.Delay(1000);
            await RefreshStatus();
        }

        private void OnImportNodesClick(object sender, RoutedEventArgs e) => InputNodes.Text = string.Join(", ", _mainNodes);
        private void OnImportKeyClick(object sender, RoutedEventArgs e) => InputKey.Text = _mainGoldenKey;

        private void Log(string msg, Brush color)
        {
            _logs.Insert(0, new FPBooster.MainWindow.LogEntry { Text = $"[{DateTime.Now:HH:mm:ss}] {msg}", Color = color });
        }
    }
}