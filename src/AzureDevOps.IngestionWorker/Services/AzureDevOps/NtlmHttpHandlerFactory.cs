using System.Net;
using System.Net.Http;
using AzureDevOps.Core.Interfaces;

namespace AzureDevOps.IngestionWorker.Services.AzureDevOps;

public static class NtlmHttpHandlerFactory
{
    public static HttpMessageHandler CreateHandler(IDynamicConfigProvider configProvider)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseDefaultCredentials = false, // Always false so it reads from Credentials property dynamically
            Credentials = new DynamicNtlmCredentials(configProvider)
        };

        return handler;
    }
}
