using System.Windows.Controls;
using Shared.Plugin;

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
            DataContext = Common.Config;
        }

        private void SaveConfig_OnClick(object sender, System.Windows.RoutedEventArgs e)
        {
            

        }
    }
}