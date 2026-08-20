using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureDevOps.Core.Models.Discovery
{
    public class ProjectsResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("value")]
        public List<TeamProjectDto> Value { get; set; } = new();
    }
}
