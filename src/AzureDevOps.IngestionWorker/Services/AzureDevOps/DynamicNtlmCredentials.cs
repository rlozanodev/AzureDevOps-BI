using System;
using System.Net;
using AzureDevOps.Core.Interfaces;

namespace AzureDevOps.IngestionWorker.Services.AzureDevOps;

public class DynamicNtlmCredentials : ICredentials
{
    private readonly IDynamicConfigProvider _configProvider;

    public DynamicNtlmCredentials(IDynamicConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public NetworkCredential? GetCredential(Uri uri, string authType)
    {
        var authOptions = _configProvider.Current.Auth;

        if (authOptions.UseDefaultCredentials)
        {
            return CredentialCache.DefaultNetworkCredentials.GetCredential(uri, authType);
        }

        if (!string.IsNullOrWhiteSpace(authOptions.Username) && !string.IsNullOrWhiteSpace(authOptions.Password))
        {
            return new NetworkCredential(
                authOptions.Username,
                authOptions.Password,
                authOptions.Domain ?? string.Empty
            );
        }

        return null;
    }
}
