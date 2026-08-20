using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AzureDevOps.DesktopManager;

public partial class App : Application
{
    public static IHost? AppHost { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build the Host (Embedding the Worker)
            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    // Here we will register everything needed. 
                    services.AddHostedService<Services.ConfigurationSyncService>();
                    // Normally we would share the IServiceCollection with the IngestionWorker setup here.
                })
                .Build();

            AppHost.Start();

            desktop.MainWindow = new Views.MainWindow();
            
            // Cancel closing to minimize to tray
            desktop.MainWindow.Closing += (s, e) =>
            {
                e.Cancel = true;
                desktop.MainWindow.Hide();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowManager_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Show();
            desktop.MainWindow?.Activate();
        }
    }

    private void ForceSync_Click(object? sender, EventArgs e)
    {
        // TO DO: Trigger sync manually
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        AppHost?.StopAsync().Wait();
        AppHost?.Dispose();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Detach event to allow closing
            if (desktop.MainWindow != null)
            {
                desktop.MainWindow.Closing -= (s, ev) => { ev.Cancel = true; desktop.MainWindow.Hide(); };
            }
            desktop.Shutdown();
        }
    }
}
