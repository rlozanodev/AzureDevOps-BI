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
    private readonly ICatalogRepository _catalogRepository;
    private readonly IPythonTransformationService _transformationService;
    private readonly IPowerBiRefreshService _powerBiService;
    private readonly IDynamicConfigProvider _configProvider;
    private readonly TransformationOptions _transformationOptions;
    private readonly PowerBiOptions _powerBiOptions;
    private readonly ILogger<IngestionOrchestratorJob> _logger;
    private CancellationTokenSource? _delayCts;

    public void ForceSync()
    {
        _delayCts?.Cancel();
    }

    public IngestionOrchestratorJob(
        IAzureDevOpsClient azureDevOpsClient,
        IWorkItemStagingRepository stagingRepository,
        ICatalogRepository catalogRepository,
        IPythonTransformationService transformationService,
        IPowerBiRefreshService powerBiService,
        IDynamicConfigProvider configProvider,
        IOptions<TransformationOptions> transformationOptions,
        IOptions<PowerBiOptions> powerBiOptions,
        ILogger<IngestionOrchestratorJob> logger)
    {
        _azureDevOpsClient = azureDevOpsClient;
        _stagingRepository = stagingRepository;
        _catalogRepository = catalogRepository;
        _transformationService = transformationService;
        _powerBiService = powerBiService;
        _configProvider = configProvider;
        _transformationOptions = transformationOptions.Value;
        _powerBiOptions = powerBiOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var devOpsOptions = await _configProvider.GetConfigAsync(stoppingToken);
        _logger.LogInformation("Azure DevOps Ingestion Orchestrator Service started. Poll interval: {Interval}s",
            devOpsOptions.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            devOpsOptions = await _configProvider.GetConfigAsync(stoppingToken);
            var collectionName = devOpsOptions.Collection;

            try
            {
                _logger.LogInformation("================================================================================");
                _logger.LogInformation("Starting Ingestion Cycle at {Time:u}", DateTime.UtcNow);

                // 1. Fase de Descubrimiento (Scraping)
                _logger.LogInformation("--- FASE 1: Descubrimiento de Proyectos ---");
                try
                {
                    var collectionsToProcess = new List<string>();
                    
                    if (string.IsNullOrWhiteSpace(collectionName))
                    {
                        _logger.LogInformation("No collection configured. Auto-discovering collections...");
                        var collections = await _azureDevOpsClient.GetCollectionsAsync(stoppingToken);
                        collectionsToProcess.AddRange(System.Linq.Enumerable.Select(collections, c => c.Name));
                    }
                    else
                    {
                        collectionsToProcess.Add(collectionName);
                    }

                    foreach (var colName in collectionsToProcess)
                    {
                        _logger.LogInformation("Descubriendo proyectos en la colección: {CollectionName}", colName);
                        try
                        {
                            var discoveredProjects = await _azureDevOpsClient.GetProjectsAsync(colName, stoppingToken);
                            await _catalogRepository.UpsertProjectsAsync(colName, discoveredProjects, stoppingToken);
                            _logger.LogInformation("Encontrados {Count} proyectos en la colección {CollectionName}.", discoveredProjects.Count, colName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error descubriendo proyectos en la colección {CollectionName}.", colName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error general durante la fase de descubrimiento.");
                    // Continues to ingestion phase with already known projects
                }

                // 2. Fase de Ingesta Aislada (Por Proyecto)
                _logger.LogInformation("--- FASE 2: Ingesta Aislada ---");
                var activeProjects = await _catalogRepository.GetEnabledProjectsAsync(stoppingToken);
                _logger.LogInformation("Proyectos activos habilitados para ingesta: {Count}", activeProjects.Count);

                foreach (var project in activeProjects)
                {
                    var syncStartUtc = DateTime.UtcNow;
                    var entityName = "work_items";
                    var projectName = project.ProjectName;
                    var currentCollectionName = project.CollectionName;

                    _logger.LogInformation("Procesando proyecto: {ProjectName} en colección: {CollectionName}", projectName, currentCollectionName);

                    try
                    {
                        var watermark = await _stagingRepository.GetWatermarkAsync(entityName, currentCollectionName, projectName, stoppingToken);
                        await _stagingRepository.UpdateWatermarkStartAsync(entityName, currentCollectionName, projectName, syncStartUtc, stoppingToken);

                        var changedSince = watermark.LastWatermarkUtc > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            ? watermark.LastWatermarkUtc
                            : (DateTime?)null;

                        var workItemIds = await _azureDevOpsClient.QueryWorkItemIdsAsync(
                            currentCollectionName,
                            projectName,
                            changedSince,
                            stoppingToken
                        );

                        var totalItemsFound = workItemIds.Count;
                        var totalSynced = 0;
                        var maxChangedDateUtc = watermark.LastWatermarkUtc;

                        if (totalItemsFound > 0)
                        {
                            await foreach (var batch in _azureDevOpsClient.StreamWorkItemBatchesAsync(
                                currentCollectionName,
                                workItemIds,
                                devOpsOptions.BatchSize,
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

                                await _stagingRepository.UpsertRawWorkItemsBatchAsync(entities, stoppingToken);
                                totalSynced += entities.Count;
                            }

                            var syncEndUtc = DateTime.UtcNow;
                            await _stagingRepository.UpdateWatermarkSuccessAsync(
                                entityName,
                                currentCollectionName,
                                projectName,
                                maxChangedDateUtc,
                                totalSynced,
                                syncEndUtc,
                                stoppingToken
                            );
                            
                            // Restore authorized access status in case it was forbidden before
                            if (project.AccessStatus != "AUTHORIZED")
                            {
                                await _catalogRepository.MarkProjectAccessStatusAsync(project.ProjectId, "AUTHORIZED", stoppingToken);
                            }

                            _logger.LogInformation("Proyecto {ProjectName} sincronizado: {Count} items.", projectName, totalSynced);
                        }
                        else
                        {
                            _logger.LogInformation("Proyecto {ProjectName} sin cambios desde {Watermark:u}.", projectName, watermark.LastWatermarkUtc);
                            await _stagingRepository.UpdateWatermarkSuccessAsync(
                                entityName,
                                currentCollectionName,
                                projectName,
                                watermark.LastWatermarkUtc,
                                0,
                                DateTime.UtcNow,
                                stoppingToken
                            );
                        }
                    }
                    catch (System.Net.Http.HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning("Acceso denegado (403) para el proyecto {ProjectName}. Se saltará en futuros ciclos.", projectName);
                        await _catalogRepository.MarkProjectAccessStatusAsync(project.ProjectId, "FORBIDDEN", stoppingToken);
                        
                        await _stagingRepository.UpdateWatermarkFailureAsync(
                            entityName,
                            currentCollectionName,
                            projectName,
                            "403 Forbidden",
                            DateTime.UtcNow,
                            stoppingToken
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al sincronizar el proyecto {ProjectName}.", projectName);
                        await _stagingRepository.UpdateWatermarkFailureAsync(
                            entityName,
                            currentCollectionName,
                            projectName,
                            ex.Message,
                            DateTime.UtcNow,
                            stoppingToken
                        );
                    }
                }

                // 3. Fase de Transformación Unificada
                _logger.LogInformation("--- FASE 3: Transformación Unificada ---");
                if (_transformationOptions.Enabled)
                {
                    _logger.LogInformation("Ejecutando DuckDB Transform...");
                    var transformResult = await _transformationService.RunTransformationAsync(stoppingToken);

                    if (transformResult.Success)
                    {
                        if (_powerBiOptions.Enabled)
                        {
                            _logger.LogInformation("Refrescando Power BI...");
                            await _powerBiService.TriggerRefreshAsync(stoppingToken);
                        }
                    }
                    else
                    {
                        _logger.LogError("Error en Transformación: {Error}", transformResult.ErrorMessage);
                    }
                }

                _logger.LogInformation("Ciclo completo. Esperando {Seconds} segundos.", devOpsOptions.PollIntervalSeconds);
                _logger.LogInformation("================================================================================");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Orquestador deteniéndose.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico en el ciclo de orquestación.");
            }

            try
            {
                using (_delayCts = new CancellationTokenSource())
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _delayCts.Token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(devOpsOptions.PollIntervalSeconds), linkedCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                _logger.LogInformation("Sync forzado por el usuario.");
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
