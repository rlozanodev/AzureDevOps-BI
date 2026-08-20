using System;
using System.Text.Json.Serialization;

namespace AzureDevOps.Core.Models.Discovery
{
    public class TeamProjectDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("revision")]
        public int Revision { get; set; }
    }
}
