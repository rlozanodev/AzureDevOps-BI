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
using AzureDevOps.IngestionWorker.Services.AzureDevOps;
using AzureDevOps.IngestionWorker.Services.Transformation;
using AzureDevOps.IngestionWorker.Services.PowerBI;
using AzureDevOps.IngestionWorker.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;
using System.Linq;

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
                    services.Configure<TransformationOptions>(config.GetSection(TransformationOptions.SectionName));
                    services.Configure<PowerBiOptions>(config.GetSection(PowerBiOptions.SectionName));

                    var devOpsConfig = config.GetSection(AzureDevOpsOptions.SectionName).Get<AzureDevOpsOptions>() ?? new AzureDevOpsOptions();

                    // ── Repositorios ─────────────────────────────────────────
                    services.AddSingleton<ICatalogRepository, CatalogRepository>();
                    services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
                    services.AddSingleton<AzureDevOps.Core.Interfaces.ILogRepository, AzureDevOps.IngestionWorker.Services.Database.LogRepository>();
                    services.AddSingleton<IWorkItemStagingRepository, WorkItemStagingRepository>();
                    services.AddSingleton<IPythonTransformationService, PythonTransformationService>();
                    services.AddSingleton<IPowerBiRefreshService, PowerBiRefreshService>();

                    services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>(client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(60);
                        client.DefaultRequestHeaders.Add("Accept", "application/json");
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider => 
                    {
                        var dynamicOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AzureDevOpsOptions>>().CurrentValue;
                        return NtlmHttpHandlerFactory.CreateHandler(dynamicOptions.Auth);
                    })
                    .SetHandlerLifetime(TimeSpan.Zero) // Force handler recreation so Auth changes take effect immediately
                    .AddPolicyHandler(GetRetryPolicy(devOpsConfig.MaxRetryAttempts, devOpsConfig.RetryBaseDelaySeconds));

                    // ── ViewModel (transient: una instancia nueva por ventana) ─
                    services.AddTransient<MainWindowViewModel>();

                    // ── Servicio de sincronización de configuración ───────────
                    services.AddHostedService<Services.ConfigurationSyncService>();
                    services.AddHostedService<IngestionOrchestratorJob>();
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
        var orchestrator = AppHost?.Services.GetServices<IHostedService>().OfType<IngestionOrchestratorJob>().FirstOrDefault();
        orchestrator?.ForceSync();
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

    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int maxRetries, double baseDelaySeconds)
    {
        var jitterer = new Random();

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => (int)msg.StatusCode == 429) // Too Many Requests / Rate Limiting
            .WaitAndRetryAsync(
                maxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(baseDelaySeconds, retryAttempt))
                              + TimeSpan.FromMilliseconds(jitterer.Next(0, 500)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    // Silent retry for UI logging
                });
    }
}

