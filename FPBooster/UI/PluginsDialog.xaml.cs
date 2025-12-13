using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using System.Collections.Generic;

namespace FPBooster.UI
{
    public partial class PluginsDialog : Window
    {
        public PluginsDialog()
        {
            InitializeComponent();
            // Анимация появления
            Loaded += (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        // --- Обработчики кнопок внутри диалога ---
        private void OpenAutoBump_Click(object sender, RoutedEventArgs e) => Launch(sender, "auto_bump");
        private void OpenLotsToggle_Click(object sender, RoutedEventArgs e) => Launch(sender, "lots_toggle");
        private void OpenLotsDelete_Click(object sender, RoutedEventArgs e) => Launch(sender, "lots_delete");
        private void OpenAutoRestock_Click(object sender, RoutedEventArgs e) => Launch(sender, "auto_restock");
        private void OpenAdvProfileStat_Click(object sender, RoutedEventArgs e) => Launch(sender, "adv_profile_stat");

        private void Launch(object sender, string pluginId)
        {
            if (Owner is MainWindow main)
            {
                RunPlugin(main, pluginId);
                Close();
            }
        }

        // --- ГЛАВНАЯ ЛОГИКА ЗАПУСКА (STATIC) ---
        public static void RunPlugin(MainWindow main, string pluginId)
        {
            var plugin = FPBooster.Plugins.PluginManager.GetById(pluginId);
            if (plugin == null)
            {
                main.Log($"[WARN] Плагин {pluginId} не найден или не загружен.");
                return;
            }

            // Получаем данные из MainWindow
            var nodes = main.GetActiveNodeIds();
            var goldenKey = main.GetGoldenKey();
            var logCollection = main.GetLogCollection();
            var themeKey = main.GetCurrentTheme();

            // 1. Настройка AutoRestock
            if (plugin is FPBooster.Plugins.AutoRestockView restockView)
            {
                restockView.InitNodes(nodes, goldenKey);
                restockView.BindLog(logCollection);
                restockView.SetTheme(themeKey);
                main.Log("[PLUGIN] Запущен: Автопополнение");
            }
            // 2. Настройка AutoBump
            else if (plugin is FPBooster.Plugins.AutoBumpView bumpView)
            {
                bumpView.InitNodes(nodes, goldenKey);
                bumpView.BindLog(logCollection);
                bumpView.SetTheme(themeKey);
                main.Log("[PLUGIN] Запущен: Авто-поднятие");
            }
            // 3. Настройка LotsToggle [НОВОЕ]
            else if (plugin is FPBooster.Plugins.LotsToggleView toggleView)
            {
                toggleView.InitNodes(nodes, goldenKey);
                toggleView.BindLog(logCollection);
                toggleView.SetTheme(themeKey);
                main.Log("[PLUGIN] Запущен: Активация лотов");
            }
            // 4. Настройка AdvProfileStat
            else if (plugin is FPBooster.Plugins.AdvProfileStatView profileStatView)
            {
                profileStatView.InitNodes(nodes, goldenKey);
                profileStatView.BindLog(logCollection);
                profileStatView.SetTheme(themeKey);
                main.Log("[PLUGIN] Запущен: Статистика профиля");
            }
            // 5. Настройка LotsDelete
            else if (plugin is FPBooster.Plugins.LotsDeleteView deleteView)
            {
                deleteView.InitNodes(nodes, goldenKey);
                deleteView.BindLog(logCollection);
                deleteView.SetTheme(themeKey);
                main.Log("[PLUGIN] Запущен: Удаление лотов");
            }

            // Переключаем интерфейс
            main.SwitchToPluginView(plugin.GetView(), plugin.DisplayName);
        }
    }
}