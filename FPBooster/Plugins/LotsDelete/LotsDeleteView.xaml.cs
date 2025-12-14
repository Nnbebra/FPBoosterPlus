using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FPBooster.Plugins;

// --- ПСЕВДОНИМЫ (FIX CS0104) ---
using UserControl = System.Windows.Controls.UserControl;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using ComboBox = System.Windows.Controls.ComboBox; // <--- ВАЖНО
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
// ------------------------------

namespace FPBooster.Plugins
{
    public partial class LotsDeleteView : UserControl, IPlugin
    {
        public string Id => "lots_delete";
        public string DisplayName => "Удаление лотов";

        private string _goldenKey;
        private ObservableCollection<FPBooster.MainWindow.LogEntry> _sharedLog;
        private List<string> _nodes = new List<string>();

        public LotsDeleteView()
        {
            InitializeComponent();
        }

        public UserControl GetView() => this;

        public void InitNodes(IEnumerable<string> nodes, string goldenKey)
        {
            _goldenKey = goldenKey;
            _nodes = nodes.ToList();
            
            if (this.FindName("NodeCombo") is ComboBox cb)
            {
                cb.SelectionChanged -= NodeCombo_SelectionChanged;
                cb.ItemsSource = _nodes;
                if (_nodes.Count > 0) cb.SelectedIndex = 0;
                cb.SelectionChanged += NodeCombo_SelectionChanged;
            }
        }

        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog)
        {
            _sharedLog = sharedLog;
        }

        public void SetTheme(string themeKey) { }

        // --- HANDLERS (FIX CS1061) ---

        private void NodeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
        {
            if (this.FindName("NodeCombo") is ComboBox cb && cb.SelectedItem != null)
                await DeleteLots(new List<string> { cb.SelectedItem.ToString() });
            else MessageBox.Show("Выберите категорию!");
        }

        private async void OnDeleteAllClick(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Удалить ВСЕ лоты?", "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                await DeleteLots(_nodes);
        }

        private void OnClearPluginLog(object sender, RoutedEventArgs e) => _sharedLog?.Clear();
        
        // Заглушки для оверлея
        private void OnOverlayBackgroundClick(object sender, RoutedEventArgs e) { }
        private void OnOverlayCancelClick(object sender, RoutedEventArgs e) { }
        private void OnOverlayConfirmClick(object sender, RoutedEventArgs e) { }

        private async Task DeleteLots(List<string> nodes)
        {
            if (string.IsNullOrEmpty(_goldenKey)) { MessageBox.Show("Нет ключа"); return; }
            try {
                AppendLog("[INFO] Запуск удаления...");
                // Заглушка, пока в Core не будет нужного метода
                await Task.Delay(1000); 
                AppendLog("[SUCCESS] Лоты удалены (Demo).");
            } catch (Exception ex) { AppendLog($"[ERR] {ex.Message}"); }
        }

        private void AppendLog(string msg)
        {
            if (_sharedLog != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _sharedLog.Insert(0, new FPBooster.MainWindow.LogEntry 
                    { 
                        Text = $"[{DateTime.Now:HH:mm:ss}] {msg}", 
                        Color = Brushes.White 
                    });
                });
            }
        }
    }
}