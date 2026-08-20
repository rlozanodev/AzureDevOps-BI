using System;

namespace AzureDevOps.Core.Entities
{
    public class CatalogProjectEntity
    {
        public Guid ProjectId { get; set; }
        public string CollectionName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ProcessTemplate { get; set; }
        public string? State { get; set; }
        public bool IsEnabled { get; set; }
        public string AccessStatus { get; set; } = string.Empty;
        public DateTime LastDiscoveredUtc { get; set; }
    }
}
