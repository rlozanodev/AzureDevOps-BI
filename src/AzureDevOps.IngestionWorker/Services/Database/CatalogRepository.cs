using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Entities;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models.Discovery;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AzureDevOps.IngestionWorker.Services.Database;

public class CatalogRepository : ICatalogRepository
{
    private readonly string _connectionString;
    private readonly ILogger<CatalogRepository> _logger;

    public CatalogRepository(
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<CatalogRepository> logger)
    {
        _connectionString = databaseOptions.Value.PostgresDb;
        _logger = logger;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task UpsertProjectsAsync(string collectionName, IEnumerable<TeamProjectDto> projects, CancellationToken ct = default)
    {
        var projectList = projects.ToList();
        if (projectList.Count == 0) return;

        // Ensure collection exists
        const string upsertCollectionSql = @"
            INSERT INTO staging.catalog_collections (collection_name, description, is_enabled, last_discovered_utc)
            VALUES (@CollectionName, @Description, true, CURRENT_TIMESTAMP)
            ON CONFLICT (collection_name) DO UPDATE SET
                last_discovered_utc = CURRENT_TIMESTAMP;";

        const string upsertProjectSql = @"
            INSERT INTO staging.catalog_projects (
                project_id, collection_name, project_name, description, 
                state, is_enabled, access_status, last_discovered_utc
            ) VALUES (
                @Id, @CollectionName, @Name, @Description, 
                @State, true, 'AUTHORIZED', CURRENT_TIMESTAMP
            )
            ON CONFLICT (project_id) DO UPDATE SET
                project_name = EXCLUDED.project_name,
                description = EXCLUDED.description,
                state = EXCLUDED.state,
                last_discovered_utc = CURRENT_TIMESTAMP;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(upsertCollectionSql, new { CollectionName = collectionName, Description = "Auto-discovered collection" }, transaction: transaction, cancellationToken: ct));

            var projectParams = projectList.Select(p => new
            {
                p.Id,
                CollectionName = collectionName,
                p.Name,
                p.Description,
                p.State
            });

            await connection.ExecuteAsync(new CommandDefinition(upsertProjectSql, projectParams, transaction: transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            _logger.LogInformation("Successfully upserted {Count} projects into catalog.", projectList.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Transaction rolled back during batch upsert of {Count} projects.", projectList.Count);
            throw;
        }
    }

    public async Task<List<CatalogProjectEntity>> GetEnabledProjectsAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                project_id AS ProjectId,
                collection_name AS CollectionName,
                project_name AS ProjectName,
                description AS Description,
                process_template AS ProcessTemplate,
                state AS State,
                is_enabled AS IsEnabled,
                access_status AS AccessStatus,
                last_discovered_utc AS LastDiscoveredUtc
            FROM staging.catalog_projects
            WHERE is_enabled = true;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        
        var results = await connection.QueryAsync<CatalogProjectEntity>(new CommandDefinition(sql, cancellationToken: ct));
        return results.ToList();
    }

    public async Task MarkProjectAccessStatusAsync(Guid projectId, string accessStatus, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE staging.catalog_projects 
            SET access_status = @AccessStatus,
                last_discovered_utc = CURRENT_TIMESTAMP
            WHERE project_id = @ProjectId;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { ProjectId = projectId, AccessStatus = accessStatus }, cancellationToken: ct));
    }
}
