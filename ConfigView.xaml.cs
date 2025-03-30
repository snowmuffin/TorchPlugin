using System.Windows;
using System.Windows.Controls;
namespace TorchPlugin
{
    // ReSharper disable once UnusedType.Global
    // ReSharper disable once RedundantExtendsListEntry
    public partial class ConfigView : UserControl
    {
        private Plugin Plugin { get; }
        public ConfigView()
        {
            InitializeComponent();
        }
        public ConfigView(Plugin plugin) : this() {
            Plugin = plugin;
            DataContext = plugin.Config;
        }
        private void SaveButton_OnClick(object sender, RoutedEventArgs e) {
            Plugin.Save();
        }
    }
}