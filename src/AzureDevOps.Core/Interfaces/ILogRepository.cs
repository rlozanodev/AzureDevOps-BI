using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AzureDevOps.Core.Interfaces;

public class SystemLogEntity
{
    public int LogId { get; set; }
    public string LogLevel { get; set; } = string.Empty;
    public string SourceContext { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Exception { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public interface ILogRepository
{
    Task InsertLogAsync(SystemLogEntity log, CancellationToken ct = default);
    Task<List<SystemLogEntity>> GetLogsByDateAsync(DateTime date, CancellationToken ct = default);
    Task<List<DateTime>> GetAvailableDatesAsync(CancellationToken ct = default);
    Task InitializeSchemaAsync();
}
