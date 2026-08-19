using AzureDevOps.Core.Models.WorkItems;

namespace AzureDevOps.Core.Interfaces;

public interface IAzureDevOpsClient
{
    /// <summary>
    /// Executes a WIQL query to retrieve all work item IDs matching the project and delta watermark.
    /// </summary>
    Task<List<int>> QueryWorkItemIdsAsync(string collection, string? project, DateTime? changedSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a batch of work items by IDs (up to 200 items per call) with $expand=all.
    /// </summary>
    Task<List<WorkItemDto>> GetWorkItemsBatchAsync(string collection, IReadOnlyList<int> workItemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams work items in chunks of batchSize (max 200) asynchronously.
    /// </summary>
    IAsyncEnumerable<List<WorkItemDto>> StreamWorkItemBatchesAsync(string collection, IReadOnlyList<int> workItemIds, int batchSize = 200, CancellationToken cancellationToken = default);
}
