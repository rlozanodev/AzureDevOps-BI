using System.Data;
using System.Text.Json;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models.Entities;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AzureDevOps.IngestionWorker.Services.Database;

public class WorkItemStagingRepository : IWorkItemStagingRepository
{
    private readonly string _connectionString;
    private readonly ILogger<WorkItemStagingRepository> _logger;

    public WorkItemStagingRepository(
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<WorkItemStagingRepository> logger)
    {
        _connectionString = databaseOptions.Value.PostgresDb;
        _logger = logger;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<SyncWatermarkEntity> GetWatermarkAsync(
        string entityName,
        string collectionName,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                entity_name AS EntityName,
                collection_name AS CollectionName,
                project_name AS ProjectName,
                last_watermark_utc AS LastWatermarkUtc,
                last_sync_start_utc AS LastSyncStartUtc,
                last_sync_end_utc AS LastSyncEndUtc,
                status AS Status,
                records_extracted_last_run AS RecordsExtractedLastRun,
                error_message AS ErrorMessage,
                updated_at_utc AS UpdatedAtUtc
            FROM staging.sync_watermarks
            WHERE entity_name = @EntityName
              AND collection_name = @CollectionName
              AND project_name = @ProjectName;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var watermark = await connection.QuerySingleOrDefaultAsync<SyncWatermarkEntity>(
            new CommandDefinition(sql, new { EntityName = entityName, CollectionName = collectionName, ProjectName = projectName }, cancellationToken: cancellationToken));

        if (watermark == null)
        {
            watermark = new SyncWatermarkEntity
            {
                EntityName = entityName,
                CollectionName = collectionName,
                ProjectName = projectName,
                LastWatermarkUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "IDLE"
            };
        }

        return watermark;
    }

    public async Task UpdateWatermarkStartAsync(
        string entityName,
        string collectionName,
        string projectName,
        DateTime syncStartUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO staging.sync_watermarks (
                entity_name, collection_name, project_name,
                last_sync_start_utc, status, updated_at_utc
            ) VALUES (
                @EntityName, @CollectionName, @ProjectName,
                @SyncStartUtc, 'RUNNING', CURRENT_TIMESTAMP
            )
            ON CONFLICT (entity_name, collection_name, project_name) DO UPDATE SET
                last_sync_start_utc = EXCLUDED.last_sync_start_utc,
                status = 'RUNNING',
                error_message = NULL,
                updated_at_utc = CURRENT_TIMESTAMP;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            EntityName = entityName,
            CollectionName = collectionName,
            ProjectName = projectName,
            SyncStartUtc = syncStartUtc
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateWatermarkSuccessAsync(
        string entityName,
        string collectionName,
        string projectName,
        DateTime watermarkUtc,
        int recordsExtracted,
        DateTime syncEndUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO staging.sync_watermarks (
                entity_name, collection_name, project_name,
                last_watermark_utc, last_sync_end_utc, status,
                records_extracted_last_run, error_message, updated_at_utc
            ) VALUES (
                @EntityName, @CollectionName, @ProjectName,
                @WatermarkUtc, @SyncEndUtc, 'SUCCESS',
                @RecordsExtracted, NULL, CURRENT_TIMESTAMP
            )
            ON CONFLICT (entity_name, collection_name, project_name) DO UPDATE SET
                last_watermark_utc = EXCLUDED.last_watermark_utc,
                last_sync_end_utc = EXCLUDED.last_sync_end_utc,
                status = 'SUCCESS',
                records_extracted_last_run = EXCLUDED.records_extracted_last_run,
                error_message = NULL,
                updated_at_utc = CURRENT_TIMESTAMP;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            EntityName = entityName,
            CollectionName = collectionName,
            ProjectName = projectName,
            WatermarkUtc = watermarkUtc,
            SyncEndUtc = syncEndUtc,
            RecordsExtracted = recordsExtracted
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateWatermarkFailureAsync(
        string entityName,
        string collectionName,
        string projectName,
        string errorMessage,
        DateTime syncEndUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO staging.sync_watermarks (
                entity_name, collection_name, project_name,
                last_sync_end_utc, status, error_message, updated_at_utc
            ) VALUES (
                @EntityName, @CollectionName, @ProjectName,
                @SyncEndUtc, 'FAILED', @ErrorMessage, CURRENT_TIMESTAMP
            )
            ON CONFLICT (entity_name, collection_name, project_name) DO UPDATE SET
                last_sync_end_utc = EXCLUDED.last_sync_end_utc,
                status = 'FAILED',
                error_message = EXCLUDED.error_message,
                updated_at_utc = CURRENT_TIMESTAMP;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            EntityName = entityName,
            CollectionName = collectionName,
            ProjectName = projectName,
            SyncEndUtc = syncEndUtc,
            ErrorMessage = errorMessage
        }, cancellationToken: cancellationToken));
    }

    public async Task<int> UpsertRawWorkItemsBatchAsync(
        IEnumerable<RawWorkItemEntity> workItems,
        CancellationToken cancellationToken = default)
    {
        var itemsList = workItems as IList<RawWorkItemEntity> ?? workItems.ToList();
        if (itemsList.Count == 0) return 0;

        const string sql = @"
            INSERT INTO staging.raw_work_items (
                id, rev, url, project_name, work_item_type, title, state, reason,
                assigned_to_name, assigned_to_unique_name, created_by_name, created_by_unique_name,
                created_date, changed_date, activated_date, closed_date, state_change_date,
                story_points, original_estimate, remaining_work, completed_work,
                priority, severity, area_path, iteration_path, tags, fields_json, ingested_at_utc
            ) VALUES (
                @Id, @Rev, @Url, @ProjectName, @WorkItemType, @Title, @State, @Reason,
                @AssignedToName, @AssignedToUniqueName, @CreatedByName, @CreatedByUniqueName,
                @CreatedDate, @ChangedDate, @ActivatedDate, @ClosedDate, @StateChangeDate,
                @StoryPoints, @OriginalEstimate, @RemainingWork, @CompletedWork,
                @Priority, @Severity, @AreaPath, @IterationPath, @Tags, @FieldsJson::jsonb, @IngestedAtUtc
            )
            ON CONFLICT (id) DO UPDATE SET
                rev = EXCLUDED.rev,
                url = EXCLUDED.url,
                project_name = EXCLUDED.project_name,
                work_item_type = EXCLUDED.work_item_type,
                title = EXCLUDED.title,
                state = EXCLUDED.state,
                reason = EXCLUDED.reason,
                assigned_to_name = EXCLUDED.assigned_to_name,
                assigned_to_unique_name = EXCLUDED.assigned_to_unique_name,
                created_by_name = EXCLUDED.created_by_name,
                created_by_unique_name = EXCLUDED.created_by_unique_name,
                created_date = EXCLUDED.created_date,
                changed_date = EXCLUDED.changed_date,
                activated_date = EXCLUDED.activated_date,
                closed_date = EXCLUDED.closed_date,
                state_change_date = EXCLUDED.state_change_date,
                story_points = EXCLUDED.story_points,
                original_estimate = EXCLUDED.original_estimate,
                remaining_work = EXCLUDED.remaining_work,
                completed_work = EXCLUDED.completed_work,
                priority = EXCLUDED.priority,
                severity = EXCLUDED.severity,
                area_path = EXCLUDED.area_path,
                iteration_path = EXCLUDED.iteration_path,
                tags = EXCLUDED.tags,
                fields_json = EXCLUDED.fields_json,
                ingested_at_utc = EXCLUDED.ingested_at_utc;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                itemsList,
                transaction: transaction,
                cancellationToken: cancellationToken
            ));

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Successfully upserted {Count} work item(s) in staging.raw_work_items.", itemsList.Count);
            return rowsAffected;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Transaction rolled back during batch upsert of {Count} work items.", itemsList.Count);
            throw;
        }
    }

    public async Task<long> GetStagingWorkItemsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM staging.raw_work_items;";
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }
}
