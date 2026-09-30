using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;

namespace AzureDevOpsAgentWeb.Services;

/// <summary>
/// Semantic Kernel plugin for Azure DevOps operations
/// </summary>
public class AzureDevOpsPlugin
{
    private readonly AzureDevOpsService _devOpsService;

    public AzureDevOpsPlugin(AzureDevOpsService devOpsService)
    {
        _devOpsService = devOpsService;
    }

    [KernelFunction("get_current_sprint_info")]
    [Description("Gets information about the current sprint including name, dates, and path")]
    public async Task<string> GetCurrentSprintInfoAsync()
    {
        try
        {
            var sprintInfo = await _devOpsService.GetSprintInfoAsync();
            
            if (sprintInfo == null)
            {
                return "No current sprint found. Please check if the team has iterations configured.";
            }

            return $"""
                Current Sprint Information:
                ===========================
                Name: {sprintInfo.Name}
                Path: {sprintInfo.Path}
                Start Date: {sprintInfo.StartDate?.ToString("MMMM dd, yyyy") ?? "Not set"}
                End Date: {sprintInfo.FinishDate?.ToString("MMMM dd, yyyy") ?? "Not set"}
                Days Remaining: {(sprintInfo.DaysRemaining > 0 ? sprintInfo.DaysRemaining.ToString() : "Sprint ended")}
                """;
        }
        catch (Exception ex)
        {
            return $"Error getting sprint info: {ex.Message}";
        }
    }

    [KernelFunction("get_sprint_work_items")]
    [Description("Gets all work items (User Stories, Bugs, Tasks) in the current sprint")]
    public async Task<string> GetSprintWorkItemsAsync()
    {
        try
        {
            var workItems = await _devOpsService.GetCurrentSprintWorkItemsAsync();

            if (workItems.Count == 0)
            {
                return "No work items found in the current sprint.";
            }

            var output = new StringBuilder();
            output.AppendLine($"Work Items in Current Sprint ({workItems.Count} total):");
            output.AppendLine();

            var grouped = workItems.GroupBy(wi => wi.WorkItemType);

            foreach (var group in grouped)
            {
                output.AppendLine($"## {group.Key}s ({group.Count()})");
                output.AppendLine();

                foreach (var item in group)
                {
                    var assignee = string.IsNullOrEmpty(item.AssignedTo) ? "Unassigned" : item.AssignedTo;
                    output.AppendLine($"- [{item.Id}] {item.Title}");
                    output.AppendLine($"  State: {item.State} | Assigned To: {assignee}");
                    output.AppendLine();
                }
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting sprint work items: {ex.Message}";
        }
    }

    [KernelFunction("get_work_item_details")]
    [Description("Gets detailed information about a specific work item by ID")]
    public async Task<string> GetWorkItemDetailsAsync(
        [Description("The work item ID")] int workItemId)
    {
        try
        {
            var workItem = await _devOpsService.GetWorkItemAsync(workItemId);

            if (workItem == null)
            {
                return $"Work item #{workItemId} not found.";
            }

            var output = new StringBuilder();
            output.AppendLine($"Work Item Details: #{workItem.Id}");
            output.AppendLine($"Type: {workItem.WorkItemType}");
            output.AppendLine($"Title: {workItem.Title}");
            output.AppendLine($"State: {workItem.State}");
            output.AppendLine($"Assigned To: {workItem.AssignedTo ?? "Unassigned"}");
            output.AppendLine($"Iteration: {workItem.IterationPath ?? "Not set"}");

            if (!string.IsNullOrEmpty(workItem.Description))
            {
                output.AppendLine();
                output.AppendLine("Description:");
                var description = System.Text.RegularExpressions.Regex.Replace(
                    workItem.Description, "<.*?>", " ");
                output.AppendLine(description.Trim());
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting work item details: {ex.Message}";
        }
    }

    [KernelFunction("create_task")]
    [Description("Creates a new Task work item, optionally linked to a parent User Story or Bug")]
    public async Task<string> CreateTaskAsync(
        [Description("Title of the task")] string title,
        [Description("Description of the task (optional)")] string? description = null,
        [Description("Parent work item ID to link to (optional)")] int? parentWorkItemId = null,
        [Description("Person to assign the task to (optional)")] string? assignedTo = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Error: Task title is required.";
            }

            var newTask = await _devOpsService.CreateTaskAsync(title, description, parentWorkItemId, assignedTo);

            if (newTask == null)
            {
                return "Error: Failed to create task.";
            }

            var output = new StringBuilder();
            output.AppendLine("? Task created successfully!");
            output.AppendLine($"ID: #{newTask.Id}");
            output.AppendLine($"Title: {newTask.Title}");
            output.AppendLine($"State: {newTask.State}");
            
            if (parentWorkItemId.HasValue)
            {
                output.AppendLine($"Linked to Parent: #{parentWorkItemId}");
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error creating task: {ex.Message}";
        }
    }

    [KernelFunction("create_multiple_tasks")]
    [Description("Creates multiple tasks under a parent work item from a comma-separated list of titles")]
    public async Task<string> CreateMultipleTasksAsync(
        [Description("Comma-separated list of task titles")] string taskTitles,
        [Description("Parent work item ID to link all tasks to")] int parentWorkItemId)
    {
        try
        {
            var titles = taskTitles.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (titles.Count == 0)
            {
                return "Error: No valid task titles provided.";
            }

            var output = new StringBuilder();
            output.AppendLine($"Creating {titles.Count} tasks under work item #{parentWorkItemId}...");
            output.AppendLine();

            var createdCount = 0;
            var failedCount = 0;

            foreach (var title in titles)
            {
                var newTask = await _devOpsService.CreateTaskAsync(title, null, parentWorkItemId);
                
                if (newTask != null)
                {
                    output.AppendLine($"? Created: [{newTask.Id}] {newTask.Title}");
                    createdCount++;
                }
                else
                {
                    output.AppendLine($"? Failed: {title}");
                    failedCount++;
                }
            }

            output.AppendLine();
            output.AppendLine($"Summary: {createdCount} created, {failedCount} failed");

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error creating tasks: {ex.Message}";
        }
    }

    [KernelFunction("update_work_item_state")]
    [Description("Updates the state of a work item (e.g., 'New', 'Active', 'Resolved', 'Closed')")]
    public async Task<string> UpdateWorkItemStateAsync(
        [Description("The work item ID to update")] int workItemId,
        [Description("The new state (e.g., 'New', 'Active', 'Resolved', 'Closed')")] string newState)
    {
        try
        {
            var updatedItem = await _devOpsService.UpdateWorkItemStateAsync(workItemId, newState);

            if (updatedItem == null)
            {
                return $"Error: Failed to update work item #{workItemId}.";
            }

            return $"""
                ? Work item updated successfully!
                ID: #{updatedItem.Id}
                Title: {updatedItem.Title}
                New State: {updatedItem.State}
                """;
        }
        catch (Exception ex)
        {
            return $"Error updating work item: {ex.Message}";
        }
    }

    [KernelFunction("get_work_items_by_state")]
    [Description("Gets work items filtered by state")]
    public async Task<string> GetWorkItemsByStateAsync(
        [Description("The state to filter by (e.g., 'New', 'Active', 'In Progress', 'Resolved')")] string state)
    {
        try
        {
            var allWorkItems = await _devOpsService.GetCurrentSprintWorkItemsAsync();
            var filteredItems = allWorkItems
                .Where(wi => wi.State.Equals(state, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filteredItems.Count == 0)
            {
                return $"No work items found with state '{state}' in the current sprint.";
            }

            var output = new StringBuilder();
            output.AppendLine($"Work Items with State '{state}' ({filteredItems.Count} items):");
            output.AppendLine();

            foreach (var item in filteredItems)
            {
                var assignee = string.IsNullOrEmpty(item.AssignedTo) ? "Unassigned" : item.AssignedTo;
                output.AppendLine($"- [{item.Id}] {item.WorkItemType}: {item.Title}");
                output.AppendLine($"  Assigned To: {assignee}");
                output.AppendLine();
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting work items: {ex.Message}";
        }
    }
}
