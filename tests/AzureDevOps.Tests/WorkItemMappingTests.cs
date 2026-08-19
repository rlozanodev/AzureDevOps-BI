using System.Text.Json;
using AzureDevOps.Core.Models.WorkItems;
using AzureDevOps.IngestionWorker.Jobs;
using FluentAssertions;
using Xunit;

namespace AzureDevOps.Tests;

public class WorkItemMappingTests
{
    [Fact]
    public void MapToEntity_ExtractsStandardAndCustomFieldsAccurately()
    {
        // Arrange
        var createdDate = new DateTime(2024, 1, 10, 10, 0, 0, DateTimeKind.Utc);
        var activatedDate = new DateTime(2024, 1, 11, 14, 0, 0, DateTimeKind.Utc);
        var closedDate = new DateTime(2024, 1, 15, 18, 30, 0, DateTimeKind.Utc);

        var dto = new WorkItemDto
        {
            Id = 42,
            Rev = 3,
            Url = "http://edvwp-tfs19-ap/DefaultCollection/_apis/wit/workitems/42",
            Fields = new Dictionary<string, object>
            {
                ["System.TeamProject"] = "CoreBanking",
                ["System.WorkItemType"] = "User Story",
                ["System.Title"] = "Implement OAuth Authentication",
                ["System.State"] = "Closed",
                ["System.Reason"] = "Completed",
                ["System.AssignedTo"] = JsonSerializer.Deserialize<JsonElement>("{\"displayName\":\"Robert Lozano\",\"uniqueName\":\"rlozano@company.com\"}"),
                ["System.CreatedBy"] = JsonSerializer.Deserialize<JsonElement>("{\"displayName\":\"Jane Smith\",\"uniqueName\":\"jsmith@company.com\"}"),
                ["System.CreatedDate"] = createdDate,
                ["System.ChangedDate"] = closedDate,
                ["Microsoft.VSTS.Common.ActivatedDate"] = activatedDate,
                ["Microsoft.VSTS.Common.ClosedDate"] = closedDate,
                ["Microsoft.VSTS.Scheduling.StoryPoints"] = 8m,
                ["Microsoft.VSTS.Scheduling.OriginalEstimate"] = 16m,
                ["Microsoft.VSTS.Scheduling.RemainingWork"] = 0m,
                ["Microsoft.VSTS.Scheduling.CompletedWork"] = 16m,
                ["Microsoft.VSTS.Common.Priority"] = 1,
                ["Microsoft.VSTS.Common.Severity"] = "2 - High",
                ["System.AreaPath"] = "CoreBanking\\Security",
                ["System.IterationPath"] = "CoreBanking\\Sprint 12",
                ["System.Tags"] = "Security; Authentication; Backend"
            }
        };

        // Act
        var entity = IngestionOrchestratorJob.MapToEntity(dto);

        // Assert
        entity.Id.Should().Be(42);
        entity.Rev.Should().Be(3);
        entity.ProjectName.Should().Be("CoreBanking");
        entity.WorkItemType.Should().Be("User Story");
        entity.Title.Should().Be("Implement OAuth Authentication");
        entity.State.Should().Be("Closed");
        entity.AssignedToName.Should().Be("Robert Lozano");
        entity.AssignedToUniqueName.Should().Be("rlozano@company.com");
        entity.CreatedByName.Should().Be("Jane Smith");
        entity.CreatedByUniqueName.Should().Be("jsmith@company.com");
        entity.CreatedDate.Should().Be(createdDate);
        entity.ActivatedDate.Should().Be(activatedDate);
        entity.ClosedDate.Should().Be(closedDate);
        entity.StoryPoints.Should().Be(8m);
        entity.Priority.Should().Be(1);
        entity.AreaPath.Should().Be("CoreBanking\\Security");
        entity.IterationPath.Should().Be("CoreBanking\\Sprint 12");
        entity.Tags.Should().Be("Security; Authentication; Backend");
        entity.FieldsJson.Should().NotBeNullOrWhiteSpace();
        entity.FieldsJson.Should().Contain("OAuth Authentication");
    }
}
