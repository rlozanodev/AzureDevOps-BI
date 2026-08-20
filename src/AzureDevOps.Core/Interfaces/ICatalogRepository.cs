using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Entities;
using AzureDevOps.Core.Models.Discovery;

namespace AzureDevOps.Core.Interfaces
{
    public interface ICatalogRepository
    {
        Task UpsertProjectsAsync(string collectionName, IEnumerable<TeamProjectDto> projects, CancellationToken ct = default);
        Task<List<CatalogProjectEntity>> GetEnabledProjectsAsync(CancellationToken ct = default);
        Task MarkProjectAccessStatusAsync(Guid projectId, string accessStatus, CancellationToken ct = default);
    }
}
