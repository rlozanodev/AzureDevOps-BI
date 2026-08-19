using System.Text.Json;
using System.Text.RegularExpressions;
using AzureDevOps.Core.Models.Wiql;
using AzureDevOps.Core.Models.WorkItems;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Seeded in-memory TFS 2019 Work Items store
var sampleProjects = new[] { "CoreBanking", "MobileApp", "PaymentGateway", "DevOpsPlatform" };
var sampleTypes = new[] { "Epic", "Feature", "User Story", "Bug", "Task" };
var sampleStates = new[] { "New", "Active", "Resolved", "Closed" };
var sampleUsers = new[]
{
    ("John Doe", "jdoe@company.local"),
    ("Jane Smith", "jsmith@company.local"),
    ("Robert Lozano", "rlozano@company.local"),
    ("Alice Johnson", "ajohnson@company.local"),
    ("Carlos Mendoza", "cmendoza@company.local")
};

var workItemsStore = new List<WorkItemDto>();
var baseDate = new DateTime(2024, 1, 15, 8, 0, 0, DateTimeKind.Utc);

// Generate 250 realistic work items to test batching (> 200 items limit)
for (int i = 1; i <= 250; i++)
{
    var project = sampleProjects[i % sampleProjects.Length];
    var type = sampleTypes[i % sampleTypes.Length];
    var user = sampleUsers[i % sampleUsers.Length];
    var creator = sampleUsers[(i + 1) % sampleUsers.Length];
    var state = sampleStates[i % sampleStates.Length];

    var createdDate = baseDate.AddDays(i * 0.5);
    var activatedDate = (state == "Active" || state == "Resolved" || state == "Closed")
        ? createdDate.AddHours(4 + (i % 48))
        : (DateTime?)null;
    var closedDate = (state == "Closed" || state == "Resolved")
        ? activatedDate?.AddHours(12 + (i % 72))
        : (DateTime?)null;
    var changedDate = closedDate ?? activatedDate ?? createdDate;

    var fields = new Dictionary<string, object>
    {
        ["System.Id"] = i,
        ["System.Rev"] = 1,
        ["System.TeamProject"] = project,
        ["System.WorkItemType"] = type,
        ["System.Title"] = $"[{type}] Implementation Item #{i} for {project}",
        ["System.State"] = state,
        ["System.Reason"] = state == "Closed" ? "Completed" : "Work started",
        ["System.AssignedTo"] = new { displayName = user.Item1, uniqueName = user.Item2 },
        ["System.CreatedBy"] = new { displayName = creator.Item1, uniqueName = creator.Item2 },
        ["System.CreatedDate"] = createdDate,
        ["System.ChangedDate"] = changedDate,
        ["System.AreaPath"] = $"{project}\\Squad {(i % 4) + 1}",
        ["System.IterationPath"] = $"{project}\\Sprint {(i % 10) + 1}",
        ["Microsoft.VSTS.Scheduling.StoryPoints"] = (decimal)((i % 8) + 1),
        ["Microsoft.VSTS.Scheduling.OriginalEstimate"] = (decimal)((i % 16) + 4),
        ["Microsoft.VSTS.Scheduling.RemainingWork"] = state == "Closed" ? 0m : (decimal)((i % 10) + 1),
        ["Microsoft.VSTS.Scheduling.CompletedWork"] = state == "Closed" ? (decimal)((i % 16) + 4) : (decimal)(i % 5),
        ["Microsoft.VSTS.Common.Priority"] = (i % 4) + 1,
        ["Microsoft.VSTS.Common.Severity"] = (i % 3 == 0) ? "2 - High" : "3 - Medium",
        ["System.Tags"] = (i % 2 == 0) ? "Backend; BI; Release2024" : "Frontend; Urgent"
    };

    if (activatedDate.HasValue) fields["Microsoft.VSTS.Common.ActivatedDate"] = activatedDate.Value;
    if (closedDate.HasValue) fields["Microsoft.VSTS.Common.ClosedDate"] = closedDate.Value;

    workItemsStore.Add(new WorkItemDto
    {
        Id = i,
        Rev = 1,
        Url = $"http://edvwp-tfs19-ap/DefaultCollection/_apis/wit/workitems/{i}",
        Fields = fields
    });
}

// 1. WIQL Endpoint
app.MapPost("/{collection}/_apis/wit/wiql", (string collection, string? apiVersion, WiqlQueryRequest request) =>
{
    return HandleWiqlQuery(request?.Query);
});

app.MapPost("/{collection}/{project}/_apis/wit/wiql", (string collection, string project, string? apiVersion, WiqlQueryRequest request) =>
{
    return HandleWiqlQuery(request?.Query);
});

// 2. Work Items Batch Endpoint
app.MapGet("/{collection}/_apis/wit/workitems", (string collection, string? ids, string? expand, string? apiVersion) =>
{
    if (string.IsNullOrWhiteSpace(ids))
    {
        return Results.Ok(new WorkItemListResponse { Count = 0, Value = new() });
    }

    var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .ToList();

    if (idList.Count > 200)
    {
        return Results.BadRequest(new { message = "Maximum batch size of 200 exceeded." });
    }

    var items = workItemsStore.Where(w => idList.Contains(w.Id)).ToList();
    return Results.Ok(new WorkItemListResponse { Count = items.Count, Value = items });
});

// 3. Health & Status
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", totalMockItems = workItemsStore.Count }));

IResult HandleWiqlQuery(string? wiql)
{
    var matching = workItemsStore.AsEnumerable();

    if (!string.IsNullOrWhiteSpace(wiql))
    {
        // Extract changed date if present: [System.ChangedDate] > '2024-01-01T00:00:00Z'
        var match = Regex.Match(wiql, @"\[System\.ChangedDate\]\s*>\s*'([^']+)'", RegexOptions.IgnoreCase);
        if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var watermark))
        {
            matching = matching.Where(w =>
            {
                if (w.Fields.TryGetValue("System.ChangedDate", out var cd) && cd is DateTime dt)
                {
                    return dt > watermark;
                }
                return true;
            });
        }

        // Project filter
        var projMatch = Regex.Match(wiql, @"\[System\.TeamProject\]\s*=\s*'([^']+)'", RegexOptions.IgnoreCase);
        if (projMatch.Success)
        {
            var proj = projMatch.Groups[1].Value;
            matching = matching.Where(w =>
                w.Fields.TryGetValue("System.TeamProject", out var p) && p?.ToString() == proj);
        }
    }

    var refs = matching.Select(w => new WiqlWorkItemReference
    {
        Id = w.Id,
        Url = w.Url
    }).ToList();

    return Results.Ok(new WiqlQueryResponse
    {
        QueryType = "flat",
        QueryResultType = "workItem",
        AsOf = DateTime.UtcNow,
        WorkItems = refs
    });
}

app.Run();
