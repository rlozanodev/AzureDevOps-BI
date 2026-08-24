using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AzureDevOps.IngestionWorker.Services.Database;

public class LogRepository : ILogRepository
{
    private readonly string _connectionString;

    public LogRepository(IOptions<DatabaseOptions> databaseOptions)
    {
        _connectionString = databaseOptions.Value.PostgresDb;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task InsertLogAsync(SystemLogEntity log, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        const string sql = @"
            INSERT INTO staging.system_logs (log_level, source_context, message, exception, created_at_utc)
            VALUES (@LogLevel, @SourceContext, @Message, @Exception, @CreatedAtUtc);";

        try 
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(sql, log, cancellationToken: ct));
        }
        catch { /* Fire and forget, ignore DB errors during logging to avoid loops */ }
    }

    public async Task<List<SystemLogEntity>> GetLogsByDateAsync(DateTime date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return new List<SystemLogEntity>();

        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = startOfDay.AddDays(1);

        const string sql = @"
            SELECT log_id AS LogId, log_level AS LogLevel, source_context AS SourceContext, 
                   message AS Message, exception AS Exception, created_at_utc AS CreatedAtUtc
            FROM staging.system_logs
            WHERE created_at_utc >= @StartOfDay AND created_at_utc < @EndOfDay
            ORDER BY created_at_utc ASC;";

        try 
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var results = await connection.QueryAsync<SystemLogEntity>(new CommandDefinition(sql, new { StartOfDay = startOfDay, EndOfDay = endOfDay }, cancellationToken: ct));
            return results.ToList();
        }
        catch { return new List<SystemLogEntity>(); }
    }

    public async Task<List<DateTime>> GetAvailableDatesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return new List<DateTime> { DateTime.UtcNow.Date };

        const string sql = @"
            SELECT DISTINCT date_trunc('day', created_at_utc) AS log_date
            FROM staging.system_logs
            ORDER BY log_date DESC;";

        try 
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            var results = await connection.QueryAsync<DateTime>(new CommandDefinition(sql, cancellationToken: ct));
            var dates = results.Select(d => d.ToLocalTime().Date).Distinct().ToList();
            if (!dates.Any()) dates.Add(DateTime.UtcNow.Date);
            return dates;
        }
        catch { return new List<DateTime> { DateTime.UtcNow.Date }; }
    }

    public async Task InitializeSchemaAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;
        const string sql = @"
            CREATE TABLE IF NOT EXISTS staging.system_logs (
                log_id SERIAL PRIMARY KEY,
                log_level VARCHAR(20) NOT NULL,
                source_context VARCHAR(255),
                message TEXT NOT NULL,
                exception TEXT,
                created_at_utc TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_system_logs_date ON staging.system_logs(created_at_utc DESC);";
        try 
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await connection.ExecuteAsync(sql);
        } catch { }
    }
}
