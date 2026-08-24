with open('src/AzureDevOps.DesktopManager/App.axaml.cs', 'r') as f:
    content = f.read()

content = content.replace('services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();', 'services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();\n                    services.AddSingleton<AzureDevOps.Core.Interfaces.ILogRepository, AzureDevOps.IngestionWorker.Services.Database.LogRepository>();')

content = content.replace('.ConfigureLogging(logging =>', '.ConfigureLogging((hostContext, logging) =>')

content = content.replace('services.AddSingleton<ILoggerProvider, Services.DatabaseLoggerProvider>();', '')
content = content.replace('.ConfigureServices((hostContext, services) =>\n                {', '.ConfigureServices((hostContext, services) =>\n                {\n                    services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, Services.DatabaseLoggerProvider>();')

content = content.replace('AppHost.Start();', 'AppHost.Start();\n            _ = System.Threading.Tasks.Task.Run(() => AppHost.Services.GetRequiredService<AzureDevOps.Core.Interfaces.ILogRepository>().InitializeSchemaAsync());')

with open('src/AzureDevOps.DesktopManager/App.axaml.cs', 'w') as f:
    f.write(content)
