using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AzureDevOps.DesktopManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;

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
        Opened += async (_, _) => await _vm.LoadProjectsAsync();
    }

    // ─── Navegación ───────────────────────────────────────────────────────────
    
    private void OnNavTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.CommandParameter is string tab)
        {
            _vm.CurrentTab = tab;
            Console.WriteLine($"[DEBUG] Changed tab to {tab}");
        }
    }

    // ─── Catálogo ─────────────────────────────────────────────────────────────

    private async void OnGuardarCambiosClick(object? sender, RoutedEventArgs e)
        => await _vm.SaveCatalogChangesAsync();

    // ─── Configuración ────────────────────────────────────────────────────────

    private async void OnTestTfsConnectionClick(object? sender, RoutedEventArgs e)
        => await _vm.TestTfsConnectionAsync();

    private async void OnGuardarConfiguracionClick(object? sender, RoutedEventArgs e)
        => await _vm.SaveConfigurationAsync();

    // ─── Logs ─────────────────────────────────────────────────────────────────
    private async void OnPrevLogDateClick(object? sender, RoutedEventArgs e)
        => await _vm.PrevLogDateAsync();

    private async void OnNextLogDateClick(object? sender, RoutedEventArgs e)
        => await _vm.NextLogDateAsync();
}
