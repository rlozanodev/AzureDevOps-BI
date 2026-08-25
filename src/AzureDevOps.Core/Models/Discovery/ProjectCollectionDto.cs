using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureDevOps.Core.Models.Discovery
{
    public class ProjectCollectionDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;
    }

    public class ProjectCollectionsResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("value")]
        public List<ProjectCollectionDto> Value { get; set; } = new();
    }
}
