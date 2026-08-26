using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzureDevOps.DesktopManager.Services;

public class ConfigurationSyncService : BackgroundService
{
    private readonly IConfigurationRepository _configurationRepository;
    private readonly ILogger<ConfigurationSyncService> _logger;
    private const string ConfigFile = "config.json";

    public ConfigurationSyncService(
        IConfigurationRepository configurationRepository,
        ILogger<ConfigurationSyncService> logger)
    {
        _configurationRepository = configurationRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Configuration Sync Service started.");

        try
        {
            // 1. Read local config if exists
            string? localConfigJson = null;
            if (File.Exists(ConfigFile))
            {
                localConfigJson = await File.ReadAllTextAsync(ConfigFile, stoppingToken);
            }

            // 2. Read DB config if DB is alive (assuming it is, this is just a mockup for the described logic)
            // Ideally we check DB connection here
            var dbConfigJson = await _configurationRepository.GetConfigurationAsync(AzureDevOpsOptions.SectionName, stoppingToken);

            if (!string.IsNullOrEmpty(dbConfigJson))
            {
                _logger.LogInformation("Loaded configuration from DB. Prioritizing DB config over local.");
                if (localConfigJson != dbConfigJson)
                {
                    await File.WriteAllTextAsync(ConfigFile, dbConfigJson, stoppingToken);
                }
            }
            else if (!string.IsNullOrEmpty(localConfigJson))
            {
                _logger.LogInformation("Loaded configuration from local file. Syncing to DB.");
                await _configurationRepository.SetConfigurationAsync(AzureDevOpsOptions.SectionName, localConfigJson, stoppingToken);
            }
            else
            {
                _logger.LogInformation("No configuration found. Awaiting user input via UI.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing configuration.");
        }

        // Keep alive or loop if needed
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(60000, stoppingToken);
        }
    }
}
