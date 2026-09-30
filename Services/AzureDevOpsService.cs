using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace AzureDevOpsAgentWeb.Services;

/// <summary>
/// Service for interacting with Azure DevOps APIs
/// </summary>
public class AzureDevOpsService
{
    private readonly VssConnection _connection;
    private readonly string _projectName;
    private readonly string? _teamName;
    private TeamSettingsIteration? teamSettingsIteration;

    public AzureDevOpsService(string organizationUrl, string personalAccessToken, string projectName, string? teamName = null)
    {
        _projectName = projectName;
        _teamName = teamName;

        var credentials = new VssBasicCredential(string.Empty, personalAccessToken);
        _connection = new VssConnection(new Uri(organizationUrl), credentials);
    }

    public async Task<TeamSettingsIteration?> GetCurrentSprintAsync()
    {
        var workClient = await _connection.GetClientAsync<WorkHttpClient>();
        var teamContext = new TeamContext(_projectName, _teamName);

        var iterations = await workClient.GetTeamIterationsAsync(teamContext, "current");
        teamSettingsIteration =  iterations.FirstOrDefault();
        return teamSettingsIteration;
    }

    public async Task<List<WorkItemInfo>> GetCurrentSprintWorkItemsAsync()
    {
        try
        {
            //var currentSprint = await GetCurrentSprintAsync();
            if (teamSettingsIteration == null)
            {
                return new List<WorkItemInfo>();
            }

            var witClient = await _connection.GetClientAsync<WorkItemTrackingHttpClient>();

            var query = $@"
            SELECT [System.Id], [System.Title], [System.State], [System.AssignedTo], [System.WorkItemType], [System.Parent]
            FROM WorkItems
            WHERE [System.TeamProject] = '{_projectName}'
            AND [System.AreaPath] UNDER '{_projectName}\\{_teamName}'
            AND [System.IterationPath] UNDER '{teamSettingsIteration.Path}'
            AND [System.State] <> 'Removed'
            ORDER BY [System.WorkItemType], [System.State]";

            var queryResult = await witClient.QueryByWiqlAsync(new Wiql { Query = query });

            if (queryResult.WorkItems == null || !queryResult.WorkItems.Any())
            {
                return new List<WorkItemInfo>();
            }

            var workItemIds = queryResult.WorkItems.Select(wi => wi.Id).ToArray();
            var workItems = await witClient.GetWorkItemsAsync(
                workItemIds,
                new[] { "System.Id", "System.Title", "System.State", "System.AssignedTo", "System.WorkItemType", "System.Parent", "System.Description" });

            return workItems.Select(wi => new WorkItemInfo
            {
                Id = wi.Id ?? 0,
                Title = wi.Fields.GetValueOrDefault("System.Title")?.ToString() ?? "Untitled",
                State = wi.Fields.GetValueOrDefault("System.State")?.ToString() ?? "Unknown",
                AssignedTo = GetAssignedToName(wi.Fields.GetValueOrDefault("System.AssignedTo")),
                WorkItemType = wi.Fields.GetValueOrDefault("System.WorkItemType")?.ToString() ?? "Unknown",
                ParentId = wi.Fields.GetValueOrDefault("System.Parent") as int?,
                Description = wi.Fields.GetValueOrDefault("System.Description")?.ToString()
            }).ToList();
        }
        catch(Exception ex)
        {
            return new List<WorkItemInfo>();
        }
    }

    public async Task<WorkItemInfo?> GetWorkItemAsync(int workItemId)
    {
        try
        {
            var witClient = await _connection.GetClientAsync<WorkItemTrackingHttpClient>();
            var workItem = await witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All);

            if (workItem == null) return null;

            return new WorkItemInfo
            {
                Id = workItem.Id ?? 0,
                Title = workItem.Fields.GetValueOrDefault("System.Title")?.ToString() ?? "Untitled",
                State = workItem.Fields.GetValueOrDefault("System.State")?.ToString() ?? "Unknown",
                AssignedTo = GetAssignedToName(workItem.Fields.GetValueOrDefault("System.AssignedTo")),
                WorkItemType = workItem.Fields.GetValueOrDefault("System.WorkItemType")?.ToString() ?? "Unknown",
                ParentId = workItem.Fields.GetValueOrDefault("System.Parent") as int?,
                Description = workItem.Fields.GetValueOrDefault("System.Description")?.ToString(),
                IterationPath = workItem.Fields.GetValueOrDefault("System.IterationPath")?.ToString(),
                AreaPath = workItem.Fields.GetValueOrDefault("System.AreaPath")?.ToString()
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<WorkItemInfo?> CreateTaskAsync(string title, string? description, int? parentWorkItemId, string? assignedTo = null)
    {
        var witClient = await _connection.GetClientAsync<WorkItemTrackingHttpClient>();
        var currentSprint = teamSettingsIteration;

        // Check if a task with the same title already exists
        var existingTask = await FindExistingTaskAsync(title, parentWorkItemId);
        if (existingTask != null)
        {
            // Task with same name already exists, return existing task info
            return existingTask;
        }

        var patchDocument = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.Title",
                Value = title
            }
        };

        if (!string.IsNullOrEmpty(description))
        {
            patchDocument.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.Description",
                Value = description
            });
        }

        if (currentSprint != null)
        {
            patchDocument.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.IterationPath",
                Value = currentSprint.Path
            });
        }

        // Set Area Path to project\team
        var areaPath = !string.IsNullOrEmpty(_teamName) 
            ? $"{_projectName}\\{_teamName}" 
            : _projectName;
        patchDocument.Add(new JsonPatchOperation
        {
            Operation = Operation.Add,
            Path = "/fields/System.AreaPath",
            Value = areaPath
        });

        if (!string.IsNullOrEmpty(assignedTo))
        {
            patchDocument.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.AssignedTo",
                Value = assignedTo
            });
        }

        var newWorkItem = await witClient.CreateWorkItemAsync(patchDocument, _projectName, "Task");

        if (parentWorkItemId.HasValue && newWorkItem?.Id != null)
        {
            var linkPatch = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/relations/-",
                    Value = new
                    {
                        rel = "System.LinkTypes.Hierarchy-Reverse",
                        url = $"{_connection.Uri}_apis/wit/workItems/{parentWorkItemId.Value}"
                    }
                }
            };

            newWorkItem = await witClient.UpdateWorkItemAsync(linkPatch, newWorkItem.Id.Value);
        }

        if (newWorkItem == null) return null;

        return new WorkItemInfo
        {
            Id = newWorkItem.Id ?? 0,
            Title = newWorkItem.Fields.GetValueOrDefault("System.Title")?.ToString() ?? title,
            State = newWorkItem.Fields.GetValueOrDefault("System.State")?.ToString() ?? "New",
            WorkItemType = "Task",
            ParentId = parentWorkItemId,
            AreaPath = areaPath
        };
    }

    /// <summary>
    /// Checks if a task with the same title already exists, optionally under a specific parent
    /// </summary>
    private async Task<WorkItemInfo?> FindExistingTaskAsync(string title, int? parentWorkItemId)
    {
        try
        {
            var witClient = await _connection.GetClientAsync<WorkItemTrackingHttpClient>();

            // Escape single quotes in title for WIQL query
            var escapedTitle = title.Replace("'", "''");

            string query;
            if (parentWorkItemId.HasValue)
            {
                // Search for task with same title that is a child of the specified parent
                query = $@"
                    SELECT [System.Id], [System.Title], [System.State], [System.AssignedTo], [System.WorkItemType]
                    FROM WorkItemLinks
                    WHERE ([Source].[System.Id] = {parentWorkItemId.Value})
                    AND ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward')
                    AND ([Target].[System.WorkItemType] = 'Task')
                    AND ([Target].[System.Title] = '{escapedTitle}')
                    AND ([Target].[System.State] <> 'Removed')
                    MODE (MustContain)";

                var linkQueryResult = await witClient.QueryByWiqlAsync(new Wiql { Query = query });

                if (linkQueryResult.WorkItemRelations != null && linkQueryResult.WorkItemRelations.Any())
                {
                    var targetRelation = linkQueryResult.WorkItemRelations
                        .FirstOrDefault(r => r.Target != null);

                    if (targetRelation?.Target != null)
                    {
                        return await GetWorkItemAsync(targetRelation.Target.Id);
                    }
                }
            }
            else
            {
                // Search for any task with the same title in the current sprint
                query = $@"
                    SELECT [System.Id], [System.Title], [System.State], [System.AssignedTo], [System.WorkItemType]
                    FROM WorkItems
                    WHERE [System.TeamProject] = '{_projectName}'
                    AND [System.WorkItemType] = 'Task'
                    AND [System.Title] = '{escapedTitle}'
                    AND [System.State] <> 'Removed'";

                if (teamSettingsIteration != null)
                {
                    query += $" AND [System.IterationPath] UNDER '{teamSettingsIteration.Path}'";
                }

                var queryResult = await witClient.QueryByWiqlAsync(new Wiql { Query = query });

                if (queryResult.WorkItems != null && queryResult.WorkItems.Any())
                {
                    var existingWorkItemId = queryResult.WorkItems.First().Id;
                    return await GetWorkItemAsync(existingWorkItemId);
                }
            }

            return null;
        }
        catch
        {
            // If check fails, allow creation to proceed
            return null;
        }
    }

    public async Task<WorkItemInfo?> UpdateWorkItemStateAsync(int workItemId, string newState)
    {
        var witClient = await _connection.GetClientAsync<WorkItemTrackingHttpClient>();

        var patchDocument = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/fields/System.State",
                Value = newState
            }
        };

        var updatedWorkItem = await witClient.UpdateWorkItemAsync(patchDocument, workItemId);

        if (updatedWorkItem == null) return null;

        return new WorkItemInfo
        {
            Id = updatedWorkItem.Id ?? 0,
            Title = updatedWorkItem.Fields.GetValueOrDefault("System.Title")?.ToString() ?? "Untitled",
            State = updatedWorkItem.Fields.GetValueOrDefault("System.State")?.ToString() ?? newState,
            WorkItemType = updatedWorkItem.Fields.GetValueOrDefault("System.WorkItemType")?.ToString() ?? "Unknown"
        };
    }

    public async Task<SprintInfo?> GetSprintInfoAsync()
    {
        var currentSprint = await GetCurrentSprintAsync();
        if (currentSprint == null) return null;

        return new SprintInfo
        {
            Name = currentSprint.Name,
            Path = currentSprint.Path,
            StartDate = currentSprint.Attributes?.StartDate,
            FinishDate = currentSprint.Attributes?.FinishDate
        };
    }

    private static string? GetAssignedToName(object? assignedTo)
    {
        if (assignedTo == null) return null;

        if (assignedTo is IdentityRef identityRef)
        {
            return identityRef.DisplayName;
        }

        return assignedTo.ToString();
    }
}

public class WorkItemInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public string WorkItemType { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string? Description { get; set; }
    public string? IterationPath { get; set; }
    public string? AreaPath { get; set; }
}

public class SprintInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? FinishDate { get; set; }
    
    public int DaysRemaining => FinishDate.HasValue 
        ? Math.Max(0, (FinishDate.Value - DateTime.Today).Days) 
        : 0;
}
