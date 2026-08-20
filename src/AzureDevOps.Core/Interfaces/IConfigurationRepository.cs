using System.Threading;
using System.Threading.Tasks;

namespace AzureDevOps.Core.Interfaces
{
    public interface IConfigurationRepository
    {
        Task<string?> GetConfigurationAsync(string configKey, CancellationToken ct = default);
        Task SetConfigurationAsync(string configKey, string configValueJson, CancellationToken ct = default);
    }
}
