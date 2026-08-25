using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace AzureDevOps.Core.Services;

public class DynamicConfigProvider : IDynamicConfigProvider
{
    private readonly IConfigurationRepository _configRepo;
    private readonly AzureDevOpsOptions _defaultOptions;
    
    // In-memory cache to allow real-time synchronous access for DelegatingHandler if needed
    public AzureDevOpsOptions Current { get; private set; }

    public DynamicConfigProvider(
        IConfigurationRepository configRepo,
        IOptions<AzureDevOpsOptions> defaultOptions)
    {
        _configRepo = configRepo;
        _defaultOptions = defaultOptions.Value;
        Current = _defaultOptions; // Initial fallback
    }

    public async Task<AzureDevOpsOptions> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var json = await _configRepo.GetConfigurationAsync(AzureDevOpsOptions.SectionName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var options = JsonSerializer.Deserialize<AzureDevOpsOptions>(json) ?? _defaultOptions;
                Current = options;
                return options;
            }
            catch
            {
                return _defaultOptions;
            }
        }
        
        return _defaultOptions;
    }

    public async Task UpdateConfigAsync(AzureDevOpsOptions newConfig, CancellationToken cancellationToken = default)
    {
        Current = newConfig;
        var json = JsonSerializer.Serialize(newConfig);
        await _configRepo.SetConfigurationAsync(AzureDevOpsOptions.SectionName, json, cancellationToken);
    }
}
