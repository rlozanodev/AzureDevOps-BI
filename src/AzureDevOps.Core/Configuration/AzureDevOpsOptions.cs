namespace AzureDevOps.Core.Configuration;

public class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    public string BaseUrl { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string ApiVersion { get; set; } = "5.0-preview";
    public int BatchSize { get; set; } = 200;
    public int PollIntervalSeconds { get; set; } = 300;
    public int MaxRetryAttempts { get; set; } = 4;
    public double RetryBaseDelaySeconds { get; set; } = 2.0;
    public int HandlerLifetimeMinutes { get; set; } = 15;
    public AzureDevOpsAuthOptions Auth { get; set; } = new();
}

public class AzureDevOpsAuthOptions
{
    /// <summary>
    /// When true, enables Windows Integrated Authentication (NTLM / Kerberos / SSPI / SSO)
    /// using DefaultNetworkCredentials / UseDefaultCredentials = true.
    /// </summary>
    public bool UseDefaultCredentials { get; set; } = true;

    /// <summary>
    /// Explicit Windows Domain for NTLM authentication (if not using current process identity)
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Explicit Username for NTLM authentication
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Explicit Password for NTLM authentication
    /// </summary>
    public string? Password { get; set; }
}
