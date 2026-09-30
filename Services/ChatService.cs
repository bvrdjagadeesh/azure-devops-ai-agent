using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

namespace AzureDevOpsAgentWeb.Services;

/// <summary>
/// Service for managing chat interactions with the AI agent
/// </summary>
public class ChatService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ChatHistory _chatHistory;

    private const string SystemPrompt = """
        You are an Azure DevOps Sprint Assistant with access to Azure DevOps APIs.
        
        Your capabilities:
        - List work items in the current sprint (User Stories, Bugs, Tasks, etc.)
        - Get details about specific work items
        - Create new tasks under existing work items
        - Update work item states
        - Provide sprint information and dates
        
        Guidelines:
        1. Always use the available tools to fetch real data from Azure DevOps
        2. When creating tasks, ask for necessary details like title and description
        3. Provide clear summaries of work items and sprint status
        4. If an operation fails, explain what happened and suggest alternatives
        5. Format responses in a clear, readable manner using markdown
        
        When listing work items, include:
        - Work Item ID
        - Title
        - State
        - Assigned To (if available)
        - Work Item Type
        """;

    public ChatService(Kernel kernel)
    {
        _kernel = kernel;
        _chatService = kernel.GetRequiredService<IChatCompletionService>();
        _chatHistory = new ChatHistory(SystemPrompt);
    }

    public void ClearHistory()
    {
        _chatHistory.Clear();
        _chatHistory.AddSystemMessage(SystemPrompt);
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(string message)
    {
        _chatHistory.AddUserMessage(message);

        var executionSettings = new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var fullResponse = string.Empty;

        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
            _chatHistory,
            executionSettings,
            _kernel))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                fullResponse += chunk.Content;
                yield return chunk.Content;
            }
        }

        if (!string.IsNullOrEmpty(fullResponse))
        {
            _chatHistory.AddAssistantMessage(fullResponse);
        }
    }

    public async Task<string> SendMessageAsync(string message)
    {
        _chatHistory.AddUserMessage(message);

        var executionSettings = new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var response = await _chatService.GetChatMessageContentAsync(
            _chatHistory,
            executionSettings,
            _kernel);

        var content = response.Content ?? string.Empty;
        _chatHistory.AddAssistantMessage(content);

        return content;
    }
}
