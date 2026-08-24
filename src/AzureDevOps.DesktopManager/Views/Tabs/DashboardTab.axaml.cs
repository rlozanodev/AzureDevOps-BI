using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;

namespace AzureDevOps.DesktopManager.Views.Tabs;

public partial class DashboardTab : UserControl
{
    public DashboardTab()
    {
        InitializeComponent();
    }
    
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;



}
