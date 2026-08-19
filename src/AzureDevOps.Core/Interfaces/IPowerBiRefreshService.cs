using AzureDevOps.Core.Models;

namespace AzureDevOps.Core.Interfaces;

public interface IPowerBiRefreshService
{
    Task<PowerBiRefreshResult> TriggerRefreshAsync(CancellationToken cancellationToken = default);
}
