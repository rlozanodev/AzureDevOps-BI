using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;

namespace AzureDevOps.Core.Interfaces;

public interface IDynamicConfigProvider
{
    AzureDevOpsOptions Current { get; }
    Task<AzureDevOpsOptions> GetConfigAsync(CancellationToken cancellationToken = default);
    Task UpdateConfigAsync(AzureDevOpsOptions newConfig, CancellationToken cancellationToken = default);
}
