using System;
using System.Windows;
using System.Windows.Media.Animation; // Для анимации
using FPBooster.Plugins;

// Устранение конфликта имен
using UserControl = System.Windows.Controls.UserControl;

namespace FPBooster.FPBoosterPlus
{
    public partial class FPBoosterPlusContainer : UserControl, IPlugin
    {
        public string Id => "fp_plus_dashboard"; 
        public string DisplayName => "FPBooster Plus";

        private readonly CloudDashboardView _dashboardView;
        private readonly CloudAutoBumpView _autoBumpView;

        public FPBoosterPlusContainer()
        {
            InitializeComponent();
            
            // Создаем экраны один раз
            _dashboardView = new CloudDashboardView();
            _autoBumpView = new CloudAutoBumpView();

            // Навигация
            _dashboardView.NavigateToAutoBump += () => NavigateTo(_autoBumpView);
            _autoBumpView.NavigateBack += () => NavigateTo(_dashboardView);

            // Показываем дашборд сразу (без анимации)
            NavigateTo(_dashboardView, false);
        }

        private void NavigateTo(UserControl view, bool animate = true)
        {
            if (!animate || MainContentArea.Content == null)
            {
                MainContentArea.Content = view;
                return;
            }

            // Анимация исчезновения старого экрана
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) => 
            {
                // Смена контента
                MainContentArea.Content = view;
                
                // Анимация появления нового экрана
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                MainContentArea.BeginAnimation(OpacityProperty, fadeIn);
            };
            
            MainContentArea.BeginAnimation(OpacityProperty, fadeOut);
        }

        public UserControl GetView() => this;

        public void InitNodes(System.Collections.Generic.IEnumerable<string> nodes, string goldenKey) 
        {
             _autoBumpView.InitNodes(nodes, goldenKey);
        }
        
        public void BindLog(System.Collections.ObjectModel.ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog) { }
        public void SetTheme(string themeKey) { }
    }
}