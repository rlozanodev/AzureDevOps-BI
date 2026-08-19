namespace AzureDevOps.Core.Models.Entities;

public class RawWorkItemEntity
{
    public int Id { get; set; }
    public int Rev { get; set; }
    public string? Url { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? AssignedToName { get; set; }
    public string? AssignedToUniqueName { get; set; }
    public string? CreatedByName { get; set; }
    public string? CreatedByUniqueName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime ChangedDate { get; set; }
    public DateTime? ActivatedDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    public DateTime? StateChangeDate { get; set; }
    public decimal? StoryPoints { get; set; }
    public decimal? OriginalEstimate { get; set; }
    public decimal? RemainingWork { get; set; }
    public decimal? CompletedWork { get; set; }
    public int? Priority { get; set; }
    public string? Severity { get; set; }
    public string? AreaPath { get; set; }
    public string? IterationPath { get; set; }
    public string? Tags { get; set; }
    public string FieldsJson { get; set; } = "{}";
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;
}
