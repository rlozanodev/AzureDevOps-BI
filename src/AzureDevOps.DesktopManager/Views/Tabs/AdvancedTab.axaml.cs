using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AzureDevOps.DesktopManager.Views.Tabs;

public partial class AdvancedTab : UserControl
{
    public AdvancedTab()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
