using System.Net;
using AzureDevOps.Core.Configuration;

namespace AzureDevOps.IngestionWorker.Services.AzureDevOps;

public static class NtlmHttpHandlerFactory
{
    public static HttpMessageHandler CreateHandler(AzureDevOpsAuthOptions authOptions)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        if (authOptions.UseDefaultCredentials)
        {
            // Windows Integrated Authentication (NTLM / Kerberos / SSO)
            handler.UseDefaultCredentials = true;
            handler.Credentials = CredentialCache.DefaultNetworkCredentials;
        }
        else if (!string.IsNullOrWhiteSpace(authOptions.Username) && !string.IsNullOrWhiteSpace(authOptions.Password))
        {
            // Explicit NTLM credentials
            handler.UseDefaultCredentials = false;
            handler.Credentials = new NetworkCredential(
                authOptions.Username,
                authOptions.Password,
                authOptions.Domain ?? string.Empty
            );
        }

        return handler;
    }
}
