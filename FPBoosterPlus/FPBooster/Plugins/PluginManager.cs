using System.Collections.Generic;
using System.Linq;
using FPBooster.FPBoosterPlus; // <--- ВАЖНО

namespace FPBooster.Plugins
{
    public static class PluginManager
    {
        private static readonly Dictionary<string, IPlugin> _plugins = new();

        static PluginManager()
        {
            // Старые плагины
            Register(new AutoRestockView());
            Register(new AutoBumpView());
            Register(new AdvProfileStatView());
            Register(new LotsDeleteView());
            Register(new LotsToggleView());
            
            // --- НОВЫЕ (CLOUD) ---
            Register(new CloudDashboardView()); // id: fp_plus_dashboard
            Register(new CloudAutoBumpView());  // id: fp_cloud_autobump
        }

        public static void Register(IPlugin plugin)
        {
            if (!_plugins.ContainsKey(plugin.Id))
                _plugins[plugin.Id] = plugin;
        }

        public static IPlugin? GetById(string id)
        {
            _plugins.TryGetValue(id, out var plugin);
            return plugin;
        }

        public static IEnumerable<IPlugin> All => _plugins.Values.ToList();
    }
}