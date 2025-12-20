using System;
using System.Windows;
using System.Windows.Controls;

// --- ИСПРАВЛЕНИЕ ОШИБКИ CS0104 ---\
using UserControl = System.Windows.Controls.UserControl;
// ---------------------------------

namespace FPBooster.FPBoosterPlus
{
    public partial class CloudDashboardView : UserControl
    {
        public event Action NavigateToAutoBump;
        public event Action NavigateToAutoRestock; // <--- НОВОЕ

        public CloudDashboardView()
        {
            InitializeComponent();
        }

        private void OpenAutoBump_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAutoBump?.Invoke();
        }

        private void OpenAutoRestock_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAutoRestock?.Invoke();
        }
    }
}