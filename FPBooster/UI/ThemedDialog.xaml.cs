using System;
using System.ComponentModel;                // для CancelEventArgs
using System.Windows;                       // для Window, RoutedEventArgs
using System.Windows.Media.Animation;       // для DoubleAnimation, QuadraticEase

namespace FPBooster.UI
{
    public partial class ThemedDialog : Window
    {
        public ThemedDialog(string title, string message)
        {
            InitializeComponent();
            TitleBlock.Text = title;
            MessageBlock.Text = message;

            Loaded += Window_Loaded;
            Closing += Window_Closing;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            e.Cancel = true;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, __) =>
            {
                Closing -= Window_Closing; // снять обработчик, чтобы не зациклиться
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
