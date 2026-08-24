using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;

namespace AzureDevOps.DesktopManager.Views.Tabs;

public partial class ConfigTab : UserControl
{
    public ConfigTab()
    {
        InitializeComponent();
    }
    
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;


    private async void OnTestTfsConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.TestTfsConnectionAsync();
    }

    private async void OnGuardarConfiguracionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SaveConfigurationAsync();
    }

}
