using System.Text.Json.Serialization;

namespace AzureDevOps.Core.Models.Wiql;

public class WiqlQueryResponse
{
    [JsonPropertyName("queryType")]
    public string? QueryType { get; set; }

    [JsonPropertyName("queryResultType")]
    public string? QueryResultType { get; set; }

    [JsonPropertyName("asOf")]
    public DateTime? AsOf { get; set; }

    [JsonPropertyName("workItems")]
    public List<WiqlWorkItemReference> WorkItems { get; set; } = new();

    [JsonPropertyName("workItemRelations")]
    public List<WiqlWorkItemRelation>? WorkItemRelations { get; set; }
}

public class WiqlWorkItemReference
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class WiqlWorkItemRelation
{
    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("source")]
    public WiqlWorkItemReference? Source { get; set; }

    [JsonPropertyName("target")]
    public WiqlWorkItemReference? Target { get; set; }
}
