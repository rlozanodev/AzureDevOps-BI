using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;
using AzureDevOps.IngestionWorker.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AzureDevOps.DesktopManager.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        // Resolve the ViewModel from the shared DI container (AppHost)
        _vm = App.AppHost!.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = _vm;

        // Load catalog projects as soon as the window opens
        Opened += async (_, _) => 
        {
            await _vm.LoadConfigurationAsync();
            await _vm.LoadProjectsAsync();
        };
    }

    // ─── Navegación ───────────────────────────────────────────────────────────
    
    private async void OnNavTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.CommandParameter is string tab)
        {
            _vm.CurrentTab = tab;
            Console.WriteLine($"[DEBUG] Changed tab to {tab}");
            
            if (tab == "3")
            {
                await _vm.LoadAvailableLogDatesAsync();
            }
        }
    }


    // ─── Tema ─────────────────────────────────────────────────────────────────
    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        var app = App.Current;
        if (app is not null)
        {
            if (app.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light)
            {
                app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            }
            else
            {
                app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            }
        }
    }

    private void OnForceSyncClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[DEBUG] ForceSync was clicked from Main Window!");
        var orchestrator = App.AppHost?.Services.GetServices<IHostedService>().OfType<IngestionOrchestratorJob>().FirstOrDefault();
        orchestrator?.ForceSync();
        
        // Optional: show a notification if possible via VM
        _vm.ShowNotification("Sincronización Iniciada", "Se ha forzado el descubrimiento de proyectos e ingesta.", false);
    }
}
