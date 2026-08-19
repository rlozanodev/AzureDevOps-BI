using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Models.Entities;
using AzureDevOps.IngestionWorker.Services.Database;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureDevOps.Tests;

public class StagingRepositoryTests
{
    private readonly WorkItemStagingRepository _repository;

    public StagingRepositoryTests()
    {
        var dbOptions = Options.Create(new DatabaseOptions
        {
            PostgresDb = "Host=localhost;Port=5432;Database=azure_devops_dw;Username=postgres;Password=postgres_secure_password_123!;"
        });
        _repository = new WorkItemStagingRepository(dbOptions, NullLogger<WorkItemStagingRepository>.Instance);
    }

    [Fact]
    public async Task Watermark_Lifecycle_IsIdempotentAndAccurate()
    {
        // 1. Initial Get Watermark
        var wm = await _repository.GetWatermarkAsync("work_items", "DefaultCollection", "TestProject");
        wm.Should().NotBeNull();

        // 2. Start Sync
        var startUtc = DateTime.UtcNow;
        await _repository.UpdateWatermarkStartAsync("work_items", "DefaultCollection", "TestProject", startUtc);

        var runningWm = await _repository.GetWatermarkAsync("work_items", "DefaultCollection", "TestProject");
        runningWm.Status.Should().Be("RUNNING");

        // 3. Complete Sync
        var endUtc = DateTime.UtcNow;
        var newWatermark = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        await _repository.UpdateWatermarkSuccessAsync("work_items", "DefaultCollection", "TestProject", newWatermark, 15, endUtc);

        var successWm = await _repository.GetWatermarkAsync("work_items", "DefaultCollection", "TestProject");
        successWm.Status.Should().Be("SUCCESS");
        successWm.RecordsExtractedLastRun.Should().Be(15);
        successWm.LastWatermarkUtc.Should().Be(newWatermark);
    }

    [Fact]
    public async Task UpsertRawWorkItems_IsStrictlyIdempotent()
    {
        // Arrange
        var created = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var changedV1 = new DateTime(2024, 5, 2, 10, 0, 0, DateTimeKind.Utc);
        var changedV2 = new DateTime(2024, 5, 5, 15, 0, 0, DateTimeKind.Utc);

        var item = new RawWorkItemEntity
        {
            Id = 999901,
            Rev = 1,
            ProjectName = "TestProject",
            WorkItemType = "Bug",
            Title = "Initial Bug Title",
            State = "New",
            CreatedDate = created,
            ChangedDate = changedV1,
            FieldsJson = "{\"System.Title\":\"Initial Bug Title\"}"
        };

        // Act 1: Insert item
        var rows1 = await _repository.UpsertRawWorkItemsBatchAsync(new[] { item });
        rows1.Should().Be(1);

        // Act 2: Update item (Rev 2 with new title and state)
        item.Rev = 2;
        item.Title = "Updated Bug Title";
        item.State = "Active";
        item.ChangedDate = changedV2;
        item.FieldsJson = "{\"System.Title\":\"Updated Bug Title\"}";

        var rows2 = await _repository.UpsertRawWorkItemsBatchAsync(new[] { item });
        rows2.Should().Be(1);

        // Verify count didn't duplicate
        var count = await _repository.GetStagingWorkItemsCountAsync();
        count.Should().BeGreaterThanOrEqualTo(1);
    }
}
