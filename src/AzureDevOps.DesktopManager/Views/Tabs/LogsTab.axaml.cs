using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;

namespace AzureDevOps.DesktopManager.Views.Tabs;

public partial class LogsTab : UserControl
{
    public LogsTab()
    {
        InitializeComponent();
    }
    
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;


    private async void OnPrevLogDateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.PrevLogDateAsync();
    }

    private async void OnNextLogDateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.NextLogDateAsync();
    }

}
