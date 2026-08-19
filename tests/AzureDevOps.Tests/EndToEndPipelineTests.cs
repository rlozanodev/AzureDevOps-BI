using AzureDevOps.Core.Configuration;
using AzureDevOps.Core.Models.Entities;
using AzureDevOps.IngestionWorker.Services.Database;
using AzureDevOps.IngestionWorker.Services.Transformation;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace AzureDevOps.Tests;

public class EndToEndPipelineTests
{
    private readonly string _connectionString = "Host=localhost;Port=5432;Database=azure_devops_dw;Username=postgres;Password=postgres_secure_password_123!;";

    [Fact]
    public async Task FullPipeline_StagingToAnalytics_CalculatesLeadAndCycleTimeCorrectly()
    {
        // 1. Seed Staging Work Items
        var dbOptions = Options.Create(new DatabaseOptions { PostgresDb = _connectionString });
        var repo = new WorkItemStagingRepository(dbOptions, NullLogger<WorkItemStagingRepository>.Instance);

        var created = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var activated = new DateTime(2024, 1, 3, 10, 0, 0, DateTimeKind.Utc); // 2 days queue time
        var closed = new DateTime(2024, 1, 10, 10, 0, 0, DateTimeKind.Utc);  // 7 days cycle time, 9 days lead time

        var items = new List<RawWorkItemEntity>
        {
            new()
            {
                Id = 88801,
                Rev = 5,
                ProjectName = "E2E_Project",
                WorkItemType = "User Story",
                Title = "E2E Completed Story",
                State = "Closed",
                AssignedToName = "Alice Dev",
                AssignedToUniqueName = "alice@e2e.test",
                CreatedByName = "Bob PO",
                CreatedByUniqueName = "bob@e2e.test",
                CreatedDate = created,
                ActivatedDate = activated,
                ClosedDate = closed,
                ChangedDate = closed,
                StoryPoints = 5m,
                Priority = 1,
                AreaPath = "E2E_Project\\Area1",
                IterationPath = "E2E_Project\\Sprint 1",
                FieldsJson = "{\"System.Title\":\"E2E Completed Story\"}"
            },
            new()
            {
                Id = 88802,
                Rev = 2,
                ProjectName = "E2E_Project",
                WorkItemType = "Bug",
                Title = "E2E Active Bug",
                State = "Active",
                AssignedToName = "Alice Dev",
                AssignedToUniqueName = "alice@e2e.test",
                CreatedByName = "Bob PO",
                CreatedByUniqueName = "bob@e2e.test",
                CreatedDate = created,
                ActivatedDate = activated,
                ClosedDate = null,
                ChangedDate = activated,
                StoryPoints = 3m,
                Priority = 2,
                AreaPath = "E2E_Project\\Area1",
                IterationPath = "E2E_Project\\Sprint 1",
                FieldsJson = "{\"System.Title\":\"E2E Active Bug\"}"
            }
        };

        await repo.UpsertRawWorkItemsBatchAsync(items);

        // 2. Execute Python + DuckDB Transformation
        var transformOptions = Options.Create(new TransformationOptions
        {
            Enabled = true,
            PythonExecutable = "uv",
            ScriptPath = "analytics_engine/transform_analytics.py"
        });
        var transformService = new PythonTransformationService(transformOptions, NullLogger<PythonTransformationService>.Instance);

        var result = await transformService.RunTransformationAsync();
        result.Success.Should().BeTrue();

        // 3. Query PostgreSQL Analytics Schema to Verify Metrics
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Verify Fact Work Items
        var factCompleted = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM analytics.fact_work_items WHERE work_item_id = 88801;");

        ((object)factCompleted).Should().NotBeNull();
        ((decimal)factCompleted.lead_time_days).Should().Be(9.00m);
        ((decimal)factCompleted.cycle_time_days).Should().Be(7.00m);
        ((decimal)factCompleted.queue_time_days).Should().Be(2.00m);
        ((bool)factCompleted.is_closed).Should().BeTrue();
        ((bool)factCompleted.is_active).Should().BeFalse();

        var factActive = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM analytics.fact_work_items WHERE work_item_id = 88802;");

        ((object)factActive).Should().NotBeNull();
        ((bool)factActive.is_closed).Should().BeFalse();
        ((bool)factActive.is_active).Should().BeTrue();
        ((decimal)factActive.wip_age_days).Should().BeGreaterThan(0);

        // Verify Dimensions Lookups
        var dimProj = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT p.project_name FROM analytics.fact_work_items f JOIN analytics.dim_project p ON f.project_key = p.project_key WHERE f.work_item_id = 88801;");
        dimProj.Should().Be("E2E_Project");

        var dimType = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT t.category FROM analytics.fact_work_items f JOIN analytics.dim_work_item_type t ON f.type_key = t.type_key WHERE f.work_item_id = 88801;");
        dimType.Should().Be("Requirement");

        // Verify Analytical Views
        var viewSummary = await conn.QueryAsync<dynamic>(
            "SELECT * FROM analytics.vw_flow_metrics_summary WHERE work_item_id IN (88801, 88802);");
        viewSummary.Should().HaveCount(2);
    }
}
