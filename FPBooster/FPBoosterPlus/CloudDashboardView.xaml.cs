using System.Windows;
using System.Windows.Controls;
using FPBooster.Plugins;

// Псевдонимы для устранения конфликтов
using UserControl = System.Windows.Controls.UserControl;
using Application = System.Windows.Application;

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudDashboardView : UserControl, IPlugin
    {
        public string Id => "fp_plus_dashboard";
        public string DisplayName => "FPBooster Plus";

        public CloudDashboardView()
        {
            InitializeComponent();
        }

        public UserControl GetView() => this;
        
        public void InitNodes(System.Collections.Generic.IEnumerable<string> nodes, string goldenKey) { }
        public void BindLog(System.Collections.ObjectModel.ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog) { }
        public void SetTheme(string themeKey) { }

        private void OpenAutoBump_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow main)
            {
                FPBooster.UI.PluginsDialog.RunPlugin(main, "fp_cloud_autobump");
            }
        }
    }
}