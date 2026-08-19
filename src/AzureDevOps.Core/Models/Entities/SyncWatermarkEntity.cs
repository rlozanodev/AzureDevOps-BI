namespace AzureDevOps.Core.Models.Entities;

public class SyncWatermarkEntity
{
    public string EntityName { get; set; } = "work_items";
    public string CollectionName { get; set; } = "DefaultCollection";
    public string ProjectName { get; set; } = string.Empty;
    public DateTime LastWatermarkUtc { get; set; } = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateTime? LastSyncStartUtc { get; set; }
    public DateTime? LastSyncEndUtc { get; set; }
    public string Status { get; set; } = "IDLE"; // IDLE, RUNNING, SUCCESS, FAILED
    public int RecordsExtractedLastRun { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
