using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Entities;
using AzureDevOps.Core.Interfaces;
using AzureDevOps.Core.Models.Entities;
using AzureDevOps.Core.Models.WorkItems;
using AzureDevOps.Core.Models.Discovery;
using AzureDevOps.IngestionWorker.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AzureDevOps.Tests;

public class OrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldContinueWhenProjectReturns403()
    {
        // Arrange
        var devOpsClientMock = new Mock<IAzureDevOpsClient>();
        var stagingRepoMock = new Mock<IWorkItemStagingRepository>();
        var catalogRepoMock = new Mock<ICatalogRepository>();
        var transformMock = new Mock<IPythonTransformationService>();
        var powerBiMock = new Mock<IPowerBiRefreshService>();

        var configProviderMock = new Mock<IDynamicConfigProvider>();
        var devOpsOptions = new AzureDevOpsOptions { Collection = "TestCol", PollIntervalSeconds = 9999, BatchSize = 200 };
        configProviderMock.Setup(m => m.Current).Returns(devOpsOptions);
        configProviderMock.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>())).ReturnsAsync(devOpsOptions);

        var transformOptions = Options.Create(new TransformationOptions { Enabled = false });
        var powerBiOptions = Options.Create(new PowerBiOptions { Enabled = false });

        var job = new IngestionOrchestratorJob(
            devOpsClientMock.Object,
            stagingRepoMock.Object,
            catalogRepoMock.Object,
            transformMock.Object,
            powerBiMock.Object,
            configProviderMock.Object,
            transformOptions,
            powerBiOptions,
            NullLogger<IngestionOrchestratorJob>.Instance
        );

        var projects = new List<CatalogProjectEntity>
        {
            new CatalogProjectEntity { ProjectId = Guid.NewGuid(), ProjectName = "ProjA", IsEnabled = true, AccessStatus = "AUTHORIZED" },
            new CatalogProjectEntity { ProjectId = Guid.NewGuid(), ProjectName = "ProjB", IsEnabled = true, AccessStatus = "AUTHORIZED" }
        };

        catalogRepoMock.Setup(x => x.GetEnabledProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(projects);

        stagingRepoMock.Setup(x => x.GetWatermarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncWatermarkEntity { LastWatermarkUtc = DateTime.UtcNow.AddDays(-1) });

        // ProjA throws 403
        devOpsClientMock.Setup(x => x.QueryWorkItemIdsAsync(It.IsAny<string>(), "ProjA", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden));

        // ProjB succeeds
        devOpsClientMock.Setup(x => x.QueryWorkItemIdsAsync(It.IsAny<string>(), "ProjB", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int> { 1 });
            
        devOpsClientMock.Setup(x => x.StreamWorkItemBatchesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(GetMockStream());

        var cts = new CancellationTokenSource();

        // Act
        // Run as background task, but cancel it soon so it doesn't run forever
        var execTask = job.StartAsync(cts.Token);
        await Task.Delay(100); // give it time to run the first loop
        cts.Cancel();
        try { await execTask; } catch { }

        // Assert
        // ProjA should have marked status as FORBIDDEN
        catalogRepoMock.Verify(x => x.MarkProjectAccessStatusAsync(projects[0].ProjectId, "FORBIDDEN", It.IsAny<CancellationToken>()), Times.Once);
        
        // ProjB should have succeeded with watermark update
        stagingRepoMock.Verify(x => x.UpdateWatermarkSuccessAsync("work_items", "TestCol", "ProjB", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private async IAsyncEnumerable<List<WorkItemDto>> GetMockStream()
    {
        yield return new List<WorkItemDto>();
        await Task.CompletedTask;
    }
}
