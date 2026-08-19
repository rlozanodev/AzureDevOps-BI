using System.Text.Json.Serialization;

namespace AzureDevOps.Core.Models.Wiql;

public class WiqlQueryRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
}
