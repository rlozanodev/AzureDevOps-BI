using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models.Wiql;
using AzureDevOps.Core.Models.WorkItems;
using AzureDevOps.Core.Models.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureDevOps.IngestionWorker.Services.AzureDevOps;

public class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly HttpClient _httpClient;
    private readonly IDynamicConfigProvider _configProvider;
    private readonly ILogger<AzureDevOpsClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AzureDevOpsClient(
        HttpClient httpClient,
        IDynamicConfigProvider configProvider,
        ILogger<AzureDevOpsClient> logger)
    {
        _httpClient = httpClient;
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task<List<int>> QueryWorkItemIdsAsync(
        string collection,
        string? project,
        DateTime? changedSinceUtc,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configProvider.Current.BaseUrl.TrimEnd('/');
        var cleanCollection = collection.Trim('/');
        
        string requestUrl;
        if (!string.IsNullOrWhiteSpace(project))
        {
            requestUrl = $"{baseUrl}/{cleanCollection}/{Uri.EscapeDataString(project)}/_apis/wit/wiql?timePrecision=true&api-version={_configProvider.Current.ApiVersion}";
        }
        else
        {
            requestUrl = $"{baseUrl}/{cleanCollection}/_apis/wit/wiql?timePrecision=true&api-version={_configProvider.Current.ApiVersion}";
        }

        string wiql;
        if (changedSinceUtc.HasValue && changedSinceUtc.Value > new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        {
            var dateStr = changedSinceUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
            wiql = string.IsNullOrWhiteSpace(project)
                ? $"SELECT [System.Id], [System.ChangedDate] FROM WorkItems WHERE [System.ChangedDate] > '{dateStr}' ORDER BY [System.ChangedDate] ASC"
                : $"SELECT [System.Id], [System.ChangedDate] FROM WorkItems WHERE [System.TeamProject] = '{project}' AND [System.ChangedDate] > '{dateStr}' ORDER BY [System.ChangedDate] ASC";
        }
        else
        {
            wiql = string.IsNullOrWhiteSpace(project)
                ? "SELECT [System.Id], [System.ChangedDate] FROM WorkItems ORDER BY [System.ChangedDate] ASC"
                : $"SELECT [System.Id], [System.ChangedDate] FROM WorkItems WHERE [System.TeamProject] = '{project}' ORDER BY [System.ChangedDate] ASC";
        }

        _logger.LogInformation("Executing WIQL query against {Url}. WIQL: {Wiql}", requestUrl, wiql);

        var payload = new WiqlQueryRequest { Query = wiql };
        var response = await _httpClient.PostAsJsonAsync(requestUrl, payload, JsonOptions, cancellationToken);
        
        response.EnsureSuccessStatusCode();

        var wiqlResult = await response.Content.ReadFromJsonAsync<WiqlQueryResponse>(JsonOptions, cancellationToken);
        if (wiqlResult == null)
        {
            _logger.LogWarning("WIQL query returned empty response body.");
            return new List<int>();
        }

        var ids = new HashSet<int>();
        if (wiqlResult.WorkItems != null && wiqlResult.WorkItems.Count > 0)
        {
            foreach (var item in wiqlResult.WorkItems)
            {
                ids.Add(item.Id);
            }
        }
        else if (wiqlResult.WorkItemRelations != null && wiqlResult.WorkItemRelations.Count > 0)
        {
            foreach (var rel in wiqlResult.WorkItemRelations)
            {
                if (rel.Source != null) ids.Add(rel.Source.Id);
                if (rel.Target != null) ids.Add(rel.Target.Id);
            }
        }

        _logger.LogInformation("WIQL query executed successfully. Found {Count} matching work item ID(s).", ids.Count);
        return ids.OrderBy(x => x).ToList();
    }

    public async Task<List<WorkItemDto>> GetWorkItemsBatchAsync(
        string collection,
        IReadOnlyList<int> workItemIds,
        CancellationToken cancellationToken = default)
    {
        if (workItemIds == null || workItemIds.Count == 0)
        {
            return new List<WorkItemDto>();
        }

        if (workItemIds.Count > 200)
        {
            throw new ArgumentException("TFS 2019 API limits batch requests to a maximum of 200 items.", nameof(workItemIds));
        }

        var baseUrl = _configProvider.Current.BaseUrl.TrimEnd('/');
        var cleanCollection = collection.Trim('/');
        var idsParam = string.Join(",", workItemIds);
        var requestUrl = $"{baseUrl}/{cleanCollection}/_apis/wit/workitems?ids={idsParam}&$expand=all&api-version={_configProvider.Current.ApiVersion}";

        _logger.LogDebug("Fetching batch of {Count} work items from {Url}", workItemIds.Count, requestUrl);

        var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WorkItemListResponse>(JsonOptions, cancellationToken);
        return result?.Value ?? new List<WorkItemDto>();
    }

    public async IAsyncEnumerable<List<WorkItemDto>> StreamWorkItemBatchesAsync(
        string collection,
        IReadOnlyList<int> workItemIds,
        int batchSize = 200,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (workItemIds == null || workItemIds.Count == 0) yield break;

        // Cap batch size at 200 per Azure DevOps Server restrictions
        var effectiveBatchSize = Math.Clamp(batchSize, 1, 200);
        var total = workItemIds.Count;

        for (int i = 0; i < total; i += effectiveBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = workItemIds.Skip(i).Take(effectiveBatchSize).ToList();
            _logger.LogInformation("Processing batch {BatchIndex}/{TotalBatches} ({Count} items)...",
                (i / effectiveBatchSize) + 1, (int)Math.Ceiling((double)total / effectiveBatchSize), chunk.Count);

            var items = await GetWorkItemsBatchAsync(collection, chunk, cancellationToken);
            yield return items;
        }
    }

    public async Task<List<TeamProjectDto>> GetProjectsAsync(string collection, CancellationToken cancellationToken = default)
    {
        var baseUrl = _configProvider.Current.BaseUrl.TrimEnd('/');
        var cleanCollection = collection.Trim('/');
        // Note: For Azure DevOps Server 2019/2020/2022, api-version for projects might need to be 5.0-preview, 
        // we'll use the API version from configuration, or append a default if needed. 
        // The plan says api-version=5.0-preview
        var requestUrl = $"{baseUrl}/{cleanCollection}/_apis/projects?api-version=5.0-preview&$top=1000";

        _logger.LogInformation("Discovering projects at {Url}", requestUrl);

        var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProjectsResponse>(JsonOptions, cancellationToken);
        return result?.Value ?? new List<TeamProjectDto>();
    }

    public async Task<List<ProjectCollectionDto>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = _configProvider.Current.BaseUrl.TrimEnd('/');
        var requestUrl = $"{baseUrl}/_apis/projectcollections?api-version={_configProvider.Current.ApiVersion}";

        _logger.LogInformation("Discovering collections at {Url}", requestUrl);

        var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProjectCollectionsResponse>(JsonOptions, cancellationToken);
        return result?.Value ?? new List<ProjectCollectionDto>();
    }
}
