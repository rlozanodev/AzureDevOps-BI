namespace AzureDevOps.Core.Configuration;

public class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string PostgresDb { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 120;
}
