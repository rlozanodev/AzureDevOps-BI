using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AzureDevOps.IngestionWorker.Services.Database;

public class ConfigurationRepository : IConfigurationRepository
{
    private readonly string _connectionString;

    public ConfigurationRepository(IOptions<DatabaseOptions> databaseOptions)
    {
        _connectionString = databaseOptions.Value.PostgresDb;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<string?> GetConfigurationAsync(string configKey, CancellationToken ct = default)
    {
        const string sql = "SELECT config_value::text FROM staging.system_configuration WHERE config_key = @ConfigKey;";
        
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new { ConfigKey = configKey }, cancellationToken: ct));
    }

    public async Task SetConfigurationAsync(string configKey, string configValueJson, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO staging.system_configuration (config_key, config_value, updated_at_utc)
            VALUES (@ConfigKey, @ConfigValue::jsonb, CURRENT_TIMESTAMP)
            ON CONFLICT (config_key) DO UPDATE SET
                config_value = EXCLUDED.config_value,
                updated_at_utc = CURRENT_TIMESTAMP;";
                
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        
        await connection.ExecuteAsync(new CommandDefinition(sql, new { ConfigKey = configKey, ConfigValue = configValueJson }, cancellationToken: ct));
    }
}
