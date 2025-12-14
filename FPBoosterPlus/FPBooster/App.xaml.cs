using System;
using System.Windows;
using FPBooster.UI;

namespace FPBooster
{
    // Явно указываем пространство имен System.Windows
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Если мы здесь, значит Application.Current инициализирован.
            // Но в Program.cs мы могли запустить MainWindow вручную.
            // Чтобы избежать двойного окна, проверяем:
            if (MainWindow == null)
            {
                Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var mainWindow = new MainWindow();
                Current.MainWindow = mainWindow;
                Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                Current.Exit += OnAppExit;
                mainWindow.Show();
            }
        }

        private void OnAppExit(object? sender, ExitEventArgs e)
        {
            if (Current.MainWindow is MainWindow mw)
            {
                mw.SaveStore();
            }
        }
    }
}