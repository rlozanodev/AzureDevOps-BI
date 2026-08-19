using System.Diagnostics;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.PowerBI.Api;
using Microsoft.Rest;

namespace AzureDevOps.IngestionWorker.Services.PowerBI;

public class PowerBiRefreshService : IPowerBiRefreshService
{
    private readonly PowerBiOptions _options;
    private readonly ILogger<PowerBiRefreshService> _logger;

    public PowerBiRefreshService(
        IOptions<PowerBiOptions> options,
        ILogger<PowerBiRefreshService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PowerBiRefreshResult> TriggerRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Power BI auto-refresh is disabled in configuration.");
            return new PowerBiRefreshResult(true, null, "Power BI refresh is disabled.", TimeSpan.Zero);
        }

        if (string.IsNullOrWhiteSpace(_options.TenantId) ||
            string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.WorkspaceId) ||
            string.IsNullOrWhiteSpace(_options.DatasetId))
        {
            _logger.LogWarning("Power BI auto-refresh is enabled but required credentials/IDs are missing.");
            return new PowerBiRefreshResult(false, null, "Missing Power BI configuration parameters.", TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Triggering Power BI dataset refresh for Workspace {WorkspaceId}, Dataset {DatasetId}...",
            _options.WorkspaceId, _options.DatasetId);

        try
        {
            var app = ConfidentialClientApplicationBuilder
                .Create(_options.ClientId)
                .WithClientSecret(_options.ClientSecret)
                .WithAuthority(new Uri(_options.AuthorityUrl))
                .Build();

            var authResult = await app
                .AcquireTokenForClient(_options.Scopes)
                .ExecuteAsync(cancellationToken);

            var tokenCredentials = new TokenCredentials(authResult.AccessToken, "Bearer");

            using var client = new PowerBIClient(new Uri("https://api.powerbi.com"), tokenCredentials);
            
            if (!Guid.TryParse(_options.WorkspaceId, out var workspaceGuid))
            {
                throw new ArgumentException($"Invalid Workspace ID (GUID expected): '{_options.WorkspaceId}'");
            }

            await client.Datasets.RefreshDatasetInGroupAsync(
                workspaceGuid,
                _options.DatasetId,
                cancellationToken: cancellationToken
            );

            stopwatch.Stop();
            _logger.LogInformation("Power BI dataset refresh triggered successfully in {Elapsed:N2}s.", stopwatch.Elapsed.TotalSeconds);

            return new PowerBiRefreshResult(
                true,
                Guid.NewGuid().ToString(),
                null,
                stopwatch.Elapsed
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to trigger Power BI dataset refresh: {Message}", ex.Message);
            return new PowerBiRefreshResult(
                false,
                null,
                ex.Message,
                stopwatch.Elapsed
            );
        }
    }
}
