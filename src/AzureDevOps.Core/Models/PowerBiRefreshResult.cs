namespace AzureDevOps.Core.Models;

public record PowerBiRefreshResult(
    bool Success,
    string? RequestId,
    string? ErrorMessage,
    TimeSpan Duration
);
