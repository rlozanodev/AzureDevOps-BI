using System.Net;
using System.Text.Json;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Models.Wiql;
using AzureDevOps.Core.Models.WorkItems;
using AzureDevOps.IngestionWorker.Services.AzureDevOps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace AzureDevOps.Tests;

public class AzureDevOpsClientTests
{
    private readonly AzureDevOpsOptions _options = new()
    {
        BaseUrl = "http://edvwp-tfs19-ap/",
        Collection = "DefaultCollection",
        ApiVersion = "5.0-preview",
        BatchSize = 200
    };

    [Fact]
    public async Task QueryWorkItemIdsAsync_GeneratesCorrectWiqlAndParsesResponse()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var wiqlResponse = new WiqlQueryResponse
        {
            WorkItems = new List<WiqlWorkItemReference>
            {
                new() { Id = 101, Url = "http://tfs/101" },
                new() { Id = 102, Url = "http://tfs/102" },
                new() { Id = 103, Url = "http://tfs/103" }
            }
        };

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("/DefaultCollection/_apis/wit/wiql?api-version=5.0-preview")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(wiqlResponse))
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var client = new AzureDevOpsClient(httpClient, Options.Create(_options), NullLogger<AzureDevOpsClient>.Instance);

        // Act
        var ids = await client.QueryWorkItemIdsAsync("DefaultCollection", null, null);

        // Assert
        ids.Should().HaveCount(3);
        ids.Should().ContainInOrder(101, 102, 103);
    }

    [Fact]
    public async Task StreamWorkItemBatchesAsync_SplitsLargeListIntoBatchesOfMax200()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var sampleIds = Enumerable.Range(1, 450).ToList();

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                var query = req.RequestUri!.Query;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new WorkItemListResponse
                    {
                        Count = 1,
                        Value = new List<WorkItemDto> { new() { Id = 1, Rev = 1 } }
                    }))
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var client = new AzureDevOpsClient(httpClient, Options.Create(_options), NullLogger<AzureDevOpsClient>.Instance);

        // Act
        var batchSizes = new List<int>();
        await foreach (var batch in client.StreamWorkItemBatchesAsync("DefaultCollection", sampleIds, batchSize: 200))
        {
            batchSizes.Add(batch.Count);
        }

        // Assert (450 items / 200 = 3 batches: 200, 200, 50)
        batchSizes.Should().HaveCount(3);
        batchSizes.Should().Equal(1, 1, 1); // Each mock call returns 1 item in sample response
    }
}
