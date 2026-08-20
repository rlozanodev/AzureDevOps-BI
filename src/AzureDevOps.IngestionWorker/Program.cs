using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.IngestionWorker.Jobs;
using AzureDevOps.IngestionWorker.Services.AzureDevOps;
using AzureDevOps.IngestionWorker.Services.Database;
using AzureDevOps.IngestionWorker.Services.PowerBI;
using AzureDevOps.IngestionWorker.Services.Transformation;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Serilog.Events;

// Configure Serilog bootstrap logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Azure DevOps BI Ingestion Worker Host...");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureAppConfiguration((hostingContext, config) =>
        {
            config.AddEnvironmentVariables();
            if (args.Length > 0)
            {
                config.AddCommandLine(args);
            }
        })
        .ConfigureServices((hostContext, services) =>
        {
            var config = hostContext.Configuration;

            // Options Configuration
            services.Configure<AzureDevOpsOptions>(config.GetSection(AzureDevOpsOptions.SectionName));
            services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));
            services.Configure<TransformationOptions>(config.GetSection(TransformationOptions.SectionName));
            services.Configure<PowerBiOptions>(config.GetSection(PowerBiOptions.SectionName));

            var devOpsConfig = config.GetSection(AzureDevOpsOptions.SectionName).Get<AzureDevOpsOptions>() ?? new AzureDevOpsOptions();

            // Repositories & Services
            services.AddSingleton<IWorkItemStagingRepository, WorkItemStagingRepository>();
            services.AddSingleton<ICatalogRepository, CatalogRepository>();
            services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();
            services.AddSingleton<IPythonTransformationService, PythonTransformationService>();
            services.AddSingleton<IPowerBiRefreshService, PowerBiRefreshService>();

            // Resilient HTTP Client with NTLM authentication and Polly retry policies
            services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>(client =>
            {
                client.BaseAddress = new Uri(devOpsConfig.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => NtlmHttpHandlerFactory.CreateHandler(devOpsConfig.Auth))
            .AddPolicyHandler(GetRetryPolicy(devOpsConfig.MaxRetryAttempts, devOpsConfig.RetryBaseDelaySeconds));

            // Main Background Ingestion Orchestrator
            services.AddHostedService<IngestionOrchestratorJob>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
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
                var statusCode = outcome.Result?.StatusCode.ToString() ?? "Exception";
                Log.Warning("HTTP transient failure ({Status}). Retrying attempt {Attempt} after {Delay:N2}s delay...",
                    statusCode, retryAttempt, timespan.TotalSeconds);
            });
}
