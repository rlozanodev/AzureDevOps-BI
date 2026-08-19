using AzureDevOps.Core.Models.Entities;

namespace AzureDevOps.Core.Interfaces;

public interface IWorkItemStagingRepository
{
    Task<SyncWatermarkEntity> GetWatermarkAsync(string entityName, string collectionName, string projectName, CancellationToken cancellationToken = default);
    Task UpdateWatermarkStartAsync(string entityName, string collectionName, string projectName, DateTime syncStartUtc, CancellationToken cancellationToken = default);
    Task UpdateWatermarkSuccessAsync(string entityName, string collectionName, string projectName, DateTime watermarkUtc, int recordsExtracted, DateTime syncEndUtc, CancellationToken cancellationToken = default);
    Task UpdateWatermarkFailureAsync(string entityName, string collectionName, string projectName, string errorMessage, DateTime syncEndUtc, CancellationToken cancellationToken = default);
    Task<int> UpsertRawWorkItemsBatchAsync(IEnumerable<RawWorkItemEntity> workItems, CancellationToken cancellationToken = default);
    Task<long> GetStagingWorkItemsCountAsync(CancellationToken cancellationToken = default);
}
