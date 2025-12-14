using System;
using System.Windows;
using System.Collections.Generic;
using System.Threading;

// --- ИСПРАВЛЕНИЕ КОНФЛИКТОВ ---
using WpfApplication = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
// ------------------------------

namespace FPBooster
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Блокировка повторного запуска
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "FPBooster_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Программа уже запущена!", "FPBooster", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (WpfApplication.Current != null)
                {
                    try 
                    {
                        WpfApplication.Current.Dispatcher.Invoke(() =>
                        {
                            try 
                            {
                                var resourceFiles = new List<string>
                                {
                                    "/FPBooster;component/UI/Themes/MidnightBlue.xaml",
                                    "/FPBooster;component/UI/Styles/Buttons.xaml",
                                    "/FPBooster;component/UI/Styles/Text.xaml",
                                    "/FPBooster;component/UI/Styles/Cards.xaml",
                                    "/FPBooster;component/UI/GlobalResources.xaml"
                                };

                                foreach (var file in resourceFiles)
                                {
                                    try 
                                    {
                                        Uri uri = new Uri(file, UriKind.Relative);
                                        ResourceDictionary dic = (ResourceDictionary)WpfApplication.LoadComponent(uri);
                                        WpfApplication.Current.Resources.MergedDictionaries.Add(dic);
                                    }
                                    catch (Exception loadEx)
                                    {
                                        MessageBox.Show($"Ошибка загрузки ресурса '{file}':\n{loadEx.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Общая ошибка ресурсов: " + ex.Message);
                            }

                            try 
                            {
                                 var mainWindow = new MainWindow(); 
                                 mainWindow.Show();
                            }
                            catch (Exception winEx)
                            {
                                 MessageBox.Show("Ошибка открытия окна: " + winEx.Message + "\n\n" + winEx.InnerException?.Message);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Critical Error: " + ex.Message);
                    }
                }
                else
                {
                    // Обычный запуск
                    var app = new App();
                    app.InitializeComponent();
                    app.Run();
                }
            }
        }
    }
}