using System.Diagnostics;
using System.Text.Json;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models.Entities;
using AzureDevOps.Core.Models.WorkItems;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOps.IngestionWorker.Jobs;

public class IngestionOrchestratorJob : BackgroundService
{
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly IWorkItemStagingRepository _stagingRepository;
    private readonly IPythonTransformationService _transformationService;
    private readonly IPowerBiRefreshService _powerBiService;
    private readonly AzureDevOpsOptions _devOpsOptions;
    private readonly TransformationOptions _transformationOptions;
    private readonly PowerBiOptions _powerBiOptions;
    private readonly ILogger<IngestionOrchestratorJob> _logger;

    public IngestionOrchestratorJob(
        IAzureDevOpsClient azureDevOpsClient,
        IWorkItemStagingRepository stagingRepository,
        IPythonTransformationService transformationService,
        IPowerBiRefreshService powerBiService,
        IOptions<AzureDevOpsOptions> devOpsOptions,
        IOptions<TransformationOptions> transformationOptions,
        IOptions<PowerBiOptions> powerBiOptions,
        ILogger<IngestionOrchestratorJob> logger)
    {
        _azureDevOpsClient = azureDevOpsClient;
        _stagingRepository = stagingRepository;
        _transformationService = transformationService;
        _powerBiService = powerBiService;
        _devOpsOptions = devOpsOptions.Value;
        _transformationOptions = transformationOptions.Value;
        _powerBiOptions = powerBiOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Azure DevOps Ingestion Orchestrator Service started. Poll interval: {Interval}s",
            _devOpsOptions.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var syncStartUtc = DateTime.UtcNow;
            var entityName = "work_items";
            var collectionName = _devOpsOptions.Collection;
            var projectName = _devOpsOptions.Project ?? string.Empty;

            try
            {
                _logger.LogInformation("================================================================================");
                _logger.LogInformation("Starting Ingestion Cycle for Collection: '{Collection}', Project: '{Project}' at {Time:u}",
                    collectionName, string.IsNullOrWhiteSpace(projectName) ? "(ALL)" : projectName, syncStartUtc);

                // 1. Fetch current watermark
                var watermark = await _stagingRepository.GetWatermarkAsync(entityName, collectionName, projectName, stoppingToken);
                _logger.LogInformation("Current Delta Watermark: {Watermark:u} (Status: {Status})",
                    watermark.LastWatermarkUtc, watermark.Status);

                await _stagingRepository.UpdateWatermarkStartAsync(entityName, collectionName, projectName, syncStartUtc, stoppingToken);

                // 2. Query Work Item IDs using WIQL (incremental delta)
                var changedSince = watermark.LastWatermarkUtc > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    ? watermark.LastWatermarkUtc
                    : (DateTime?)null;

                var workItemIds = await _azureDevOpsClient.QueryWorkItemIdsAsync(
                    collectionName,
                    projectName,
                    changedSince,
                    stoppingToken
                );

                var totalItemsFound = workItemIds.Count;
                var totalSynced = 0;
                var maxChangedDateUtc = watermark.LastWatermarkUtc;

                if (totalItemsFound > 0)
                {
                    _logger.LogInformation("Discovered {Count} work item(s) to synchronize in batches of max {BatchSize}...",
                        totalItemsFound, _devOpsOptions.BatchSize);

                    // 3. Stream & Process in Batches of max 200
                    await foreach (var batch in _azureDevOpsClient.StreamWorkItemBatchesAsync(
                        collectionName,
                        workItemIds,
                        _devOpsOptions.BatchSize,
                        stoppingToken))
                    {
                        var entities = new List<RawWorkItemEntity>(batch.Count);

                        foreach (var dto in batch)
                        {
                            var entity = MapToEntity(dto);
                            entities.Add(entity);

                            if (entity.ChangedDate > maxChangedDateUtc)
                            {
                                maxChangedDateUtc = entity.ChangedDate;
                            }
                        }

                        // 4. Idempotent Upsert to Staging
                        var rowsAffected = await _stagingRepository.UpsertRawWorkItemsBatchAsync(entities, stoppingToken);
                        totalSynced += entities.Count;

                        _logger.LogInformation("Batch persisted. Progress: {TotalSynced}/{TotalItemsFound} items.",
                            totalSynced, totalItemsFound);
                    }

                    var syncEndUtc = DateTime.UtcNow;
                    await _stagingRepository.UpdateWatermarkSuccessAsync(
                        entityName,
                        collectionName,
                        projectName,
                        maxChangedDateUtc,
                        totalSynced,
                        syncEndUtc,
                        stoppingToken
                    );

                    _logger.LogInformation("Ingestion staging completed successfully. Synced {Count} items in {Duration:N2}s. New Watermark: {Watermark:u}",
                        totalSynced, (syncEndUtc - syncStartUtc).TotalSeconds, maxChangedDateUtc);
                }
                else
                {
                    _logger.LogInformation("No new or modified work items found since {Watermark:u}.", watermark.LastWatermarkUtc);
                    await _stagingRepository.UpdateWatermarkSuccessAsync(
                        entityName,
                        collectionName,
                        projectName,
                        watermark.LastWatermarkUtc,
                        0,
                        DateTime.UtcNow,
                        stoppingToken
                    );
                }

                // 5. Trigger Transformation Engine (Python + DuckDB)
                if (_transformationOptions.Enabled)
                {
                    _logger.LogInformation("Executing OLAP Transformation Engine (DuckDB)...");
                    var transformResult = await _transformationService.RunTransformationAsync(stoppingToken);

                    if (transformResult.Success)
                    {
                        // 6. Trigger Power BI Dataset Refresh
                        if (_powerBiOptions.Enabled)
                        {
                            _logger.LogInformation("Triggering Power BI dataset refresh...");
                            await _powerBiService.TriggerRefreshAsync(stoppingToken);
                        }
                    }
                    else
                    {
                        _logger.LogError("OLAP Transformation encountered errors: {Error}", transformResult.ErrorMessage);
                    }
                }

                _logger.LogInformation("Ingestion Cycle finished cleanly. Next cycle in {Seconds} seconds.",
                    _devOpsOptions.PollIntervalSeconds);
                _logger.LogInformation("================================================================================");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Ingestion Worker is shutting down as requested.");
                break;
            }
            catch (Exception ex)
            {
                var syncEndUtc = DateTime.UtcNow;
                _logger.LogError(ex, "Error during Azure DevOps ingestion cycle: {Message}", ex.Message);

                try
                {
                    await _stagingRepository.UpdateWatermarkFailureAsync(
                        entityName,
                        collectionName,
                        projectName,
                        ex.Message,
                        syncEndUtc,
                        CancellationToken.None
                    );
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Failed to record sync failure state in database.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_devOpsOptions.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public static RawWorkItemEntity MapToEntity(WorkItemDto dto)
    {
        var entity = new RawWorkItemEntity
        {
            Id = dto.Id,
            Rev = dto.Rev,
            Url = dto.Url,
            ProjectName = dto.GetFieldValue<string>("System.TeamProject") ?? "Unknown",
            WorkItemType = dto.GetFieldValue<string>("System.WorkItemType") ?? "Unknown",
            Title = dto.GetFieldValue<string>("System.Title"),
            State = dto.GetFieldValue<string>("System.State") ?? "Unknown",
            Reason = dto.GetFieldValue<string>("System.Reason"),
            AssignedToName = dto.GetIdentityName("System.AssignedTo"),
            AssignedToUniqueName = dto.GetIdentityUniqueName("System.AssignedTo"),
            CreatedByName = dto.GetIdentityName("System.CreatedBy"),
            CreatedByUniqueName = dto.GetIdentityUniqueName("System.CreatedBy"),
            CreatedDate = dto.GetFieldValue<DateTime?>("System.CreatedDate"),
            ChangedDate = dto.GetFieldValue<DateTime?>("System.ChangedDate") ?? DateTime.UtcNow,
            ActivatedDate = dto.GetFieldValue<DateTime?>("Microsoft.VSTS.Common.ActivatedDate"),
            ClosedDate = dto.GetFieldValue<DateTime?>("Microsoft.VSTS.Common.ClosedDate"),
            StateChangeDate = dto.GetFieldValue<DateTime?>("Microsoft.VSTS.Common.StateChangeDate"),
            StoryPoints = dto.GetFieldValue<decimal?>("Microsoft.VSTS.Scheduling.StoryPoints"),
            OriginalEstimate = dto.GetFieldValue<decimal?>("Microsoft.VSTS.Scheduling.OriginalEstimate"),
            RemainingWork = dto.GetFieldValue<decimal?>("Microsoft.VSTS.Scheduling.RemainingWork"),
            CompletedWork = dto.GetFieldValue<decimal?>("Microsoft.VSTS.Scheduling.CompletedWork"),
            Priority = dto.GetFieldValue<int?>("Microsoft.VSTS.Common.Priority"),
            Severity = dto.GetFieldValue<string>("Microsoft.VSTS.Common.Severity"),
            AreaPath = dto.GetFieldValue<string>("System.AreaPath"),
            IterationPath = dto.GetFieldValue<string>("System.IterationPath"),
            Tags = dto.GetFieldValue<string>("System.Tags"),
            FieldsJson = JsonSerializer.Serialize(dto.Fields),
            IngestedAtUtc = DateTime.UtcNow
        };

        return entity;
    }
}
