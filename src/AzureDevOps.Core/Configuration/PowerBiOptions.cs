namespace AzureDevOps.Core.Configuration;

public class PowerBiOptions
{
    public const string SectionName = "PowerBi";

    public bool Enabled { get; set; } = false;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string DatasetId { get; set; } = string.Empty;
    public string AuthorityUrl => $"https://login.microsoftonline.com/{TenantId}";
    public string[] Scopes { get; set; } = ["https://analysis.windows.net/powerbi/api/.default"];
}
