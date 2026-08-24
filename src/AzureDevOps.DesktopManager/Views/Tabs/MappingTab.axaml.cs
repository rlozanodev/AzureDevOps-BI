using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;

namespace AzureDevOps.DesktopManager.Views.Tabs;

public partial class MappingTab : UserControl
{
    public MappingTab()
    {
        InitializeComponent();
    }
    
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async void OnGuardarCambiosClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.SaveCatalogChangesAsync();
    }
}
