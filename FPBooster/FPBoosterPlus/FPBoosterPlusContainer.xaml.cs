using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Animation;
using FPBooster.Plugins;

using UserControl = System.Windows.Controls.UserControl;

namespace FPBooster.FPBoosterPlus
{
    public partial class FPBoosterPlusContainer : UserControl, IPlugin
    {
        public string Id => "fp_plus_dashboard"; 
        public string DisplayName => "FPBooster Plus";

        // Общий лог
        private readonly ObservableCollection<FPBooster.MainWindow.LogEntry> _sharedCloudLog = new ObservableCollection<FPBooster.MainWindow.LogEntry>();

        private readonly CloudDashboardView _dashboardView;
        private readonly CloudAutoBumpView _autoBumpView;
        private readonly CloudAutoRestockView _restockView;

        public FPBoosterPlusContainer()
        {
            InitializeComponent();
            
            _dashboardView = new CloudDashboardView();
            
            // Инициализация БЕЗ аргументов
            _autoBumpView = new CloudAutoBumpView();
            _restockView = new CloudAutoRestockView();

            // Передаем лог вручную
            _autoBumpView.SetSharedLog(_sharedCloudLog);
            _restockView.SetSharedLog(_sharedCloudLog);

            // Навигация
            _dashboardView.NavigateToAutoBump += () => NavigateTo(_autoBumpView);
            _dashboardView.NavigateToAutoRestock += () => NavigateTo(_restockView);

            _autoBumpView.NavigateBack += () => NavigateTo(_dashboardView);
            _restockView.NavigateBack += () => NavigateTo(_dashboardView);

            NavigateTo(_dashboardView, false);
        }

        private void NavigateTo(UserControl view, bool animate = true)
        {
            if (!animate || MainContentArea.Content == null)
            {
                MainContentArea.Content = view;
                return;
            }
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) => {
                MainContentArea.Content = view;
                MainContentArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            };
            MainContentArea.BeginAnimation(OpacityProperty, fadeOut);
        }

        public UserControl GetView() => this;
        public void InitNodes(System.Collections.Generic.IEnumerable<string> nodes, string goldenKey) => _autoBumpView.InitNodes(nodes, goldenKey);
        public void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog) { }
        public void SetTheme(string themeKey) { }
    }
}