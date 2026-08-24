using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.DesktopManager.ViewModels;
using AzureDevOps.IngestionWorker.Services.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            // Buscar el appsettings.json del Worker (hermano en el árbol de directorios)
            var workerSettingsPath = Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", // salir de bin/Debug/net9.0
                "AzureDevOps.IngestionWorker",
                "appsettings.json");

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    // 1. appsettings.json del Worker como fuente base
                    var resolvedPath = Path.GetFullPath(workerSettingsPath);
                    if (File.Exists(resolvedPath))
                        config.AddJsonFile(resolvedPath, optional: false, reloadOnChange: false);

                    // 2. Variables de entorno sobreescriben (ideal para producción / Docker)
                    config.AddEnvironmentVariables();
                })
                .ConfigureLogging((hostContext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddSimpleConsole(options => 
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "[HH:mm:ss] ";
                    });
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, Services.DatabaseLoggerProvider>();
                    var config = hostContext.Configuration;

                    // ── Opciones ─────────────────────────────────────────────
                    services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));
                    services.Configure<AzureDevOpsOptions>(config.GetSection(AzureDevOpsOptions.SectionName));

                    // ── Repositorios ─────────────────────────────────────────
                    services.AddSingleton<ICatalogRepository, CatalogRepository>();
                    services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
                    services.AddSingleton<AzureDevOps.Core.Interfaces.ILogRepository, AzureDevOps.IngestionWorker.Services.Database.LogRepository>();

                    // ── ViewModel (transient: una instancia nueva por ventana) ─
                    services.AddTransient<MainWindowViewModel>();

                    // ── Servicio de sincronización de configuración ───────────
                    services.AddHostedService<Services.ConfigurationSyncService>();
                })
                .Build();

            AppHost.Start();
            _ = System.Threading.Tasks.Task.Run(() => AppHost.Services.GetRequiredService<AzureDevOps.Core.Interfaces.ILogRepository>().InitializeSchemaAsync());

            desktop.MainWindow = new Views.MainWindow();

            // Minimizar al tray al cerrar la X
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
        Console.WriteLine("[DEBUG] ForceSync was clicked from Tray Icon!");
        // TO DO: Trigger sync manually via IConfigurationRepository
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        AppHost?.StopAsync().Wait();
        AppHost?.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
            {
                desktop.MainWindow.Closing -= (s, ev) => { ev.Cancel = true; desktop.MainWindow.Hide(); };
            }
            desktop.Shutdown();
        }
    }
}

