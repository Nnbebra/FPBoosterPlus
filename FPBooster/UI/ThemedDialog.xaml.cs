using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

// --- ПСЕВДОНИМЫ (FIX CS0104) ---
using Button = System.Windows.Controls.Button;
// ------------------------------

namespace FPBooster.UI
{
    public partial class ThemedDialog : Window
    {
        private bool _isClosing = false;

        // Добавлена логика для режима подтверждения (Yes/No)
        public ThemedDialog(string title, string message, bool isConfirmation = false)
        {
            InitializeComponent();
            
            // Безопасная установка текста через FindName
            if (this.FindName("TitleBlock") is TextBlock tbTitle) tbTitle.Text = title;
            if (this.FindName("MessageBlock") is TextBlock tbMsg) tbMsg.Text = message;

            // Настройка кнопок
            var btnOk = this.FindName("BtnOk") as Button;
            var btnCancel = this.FindName("BtnCancel") as Button;

            if (isConfirmation)
            {
                if (btnOk != null) btnOk.Content = "ДА";
                if (btnCancel != null) 
                {
                    btnCancel.Visibility = Visibility.Visible;
                    btnCancel.Content = "НЕТ";
                }
            }

            // Анимация при запуске
            Loaded += (s, e) => 
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => CloseWithResult(true);
        private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWithResult(false);

        // Корректное закрытие с анимацией
        private void CloseWithResult(bool result)
        {
            if (_isClosing) return;
            _isClosing = true;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) => 
            {
                DialogResult = result; // Это автоматически закроет окно
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}