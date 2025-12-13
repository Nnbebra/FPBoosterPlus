// Добавляем псевдоним
using UserControl = System.Windows.Controls.UserControl;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Для ObservableCollection

namespace FPBooster.Plugins
{
    public interface IPlugin
    {
        string Id { get; }
        string DisplayName { get; }
        UserControl GetView();
        
        void InitNodes(IEnumerable<string> nodes, string goldenKey);
        void BindLog(ObservableCollection<FPBooster.MainWindow.LogEntry> sharedLog);
        void SetTheme(string themeKey);
    }
}