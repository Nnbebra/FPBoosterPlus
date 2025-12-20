using System.Collections.Generic;
using System.Linq;
using FPBooster.FPBoosterPlus;

namespace FPBooster.Plugins
{
    public static class PluginManager
    {
        private static readonly Dictionary<string, IPlugin> _plugins = new Dictionary<string, IPlugin>();

        static PluginManager()
        {
            // Стандартные плагины
            Register(new AutoRestockView());
            Register(new AutoBumpView());
            Register(new AdvProfileStatView());
            Register(new LotsDeleteView());
            Register(new LotsToggleView());
            
            // --- НОВОЕ (CLOUD) ---
            // Регистрируем ТОЛЬКО контейнер. Он реализует IPlugin с ID "fp_plus_dashboard".
            // Отдельные View (Dashboard/AutoBump) больше не регистрируем, они живут внутри контейнера.
            Register(new FPBoosterPlusContainer()); 
        }

        public static void Register(IPlugin plugin)
        {
            if (!_plugins.ContainsKey(plugin.Id))
                _plugins[plugin.Id] = plugin;
        }

        public static IPlugin? GetById(string id)
        {
            // ПЕРЕАДРЕСАЦИЯ:
            // Если запрашивают ID "fp_cloud_autobump" (например, по кнопке из другого места),
            // мы возвращаем наш главный контейнер ("fp_plus_dashboard"), 
            // так как теперь он управляет отображением.
            if (id == "fp_cloud_autobump" || id == "fp_plus_dashboard")
            {
                return _plugins.ContainsKey("fp_plus_dashboard") ? _plugins["fp_plus_dashboard"] : null;
            }

            _plugins.TryGetValue(id, out var plugin);
            return plugin;
        }

        public static IEnumerable<IPlugin> All => _plugins.Values.ToList();
    }
}