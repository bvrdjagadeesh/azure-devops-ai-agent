using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;

namespace AzureDevOpsAgentWeb.Services;

/// <summary>
/// Holds the runtime configuration for Azure DevOps
/// </summary>
public class AppConfiguration
{
    // Azure DevOps Settings (user provided)
    public string? OrganizationUrl { get; set; }
    public string? PersonalAccessToken { get; set; }
    public string? ProjectName { get; set; }
    public string? TeamName { get; set; }

    public bool IsDevOpsConfigured =>
        !string.IsNullOrEmpty(OrganizationUrl) &&
        !string.IsNullOrEmpty(PersonalAccessToken) &&
        !string.IsNullOrEmpty(ProjectName);
}

/// <summary>
/// Service to manage application configuration state
/// </summary>
public class ConfigurationStateService
{
    private readonly AppConfiguration _configuration = new();
    private readonly IConfiguration _appSettings;
    private AzureDevOpsService? _devOpsService;
    private ChatService? _chatService;

    public event Action? OnConfigurationChanged;

    public AppConfiguration Configuration => _configuration;

    public bool IsConfigured => _configuration.IsDevOpsConfigured && IsOpenAIConfigured;

    public bool IsOpenAIConfigured =>
        !string.IsNullOrEmpty(_appSettings["AzureOpenAI:Endpoint"]) &&
        !string.IsNullOrEmpty(_appSettings["AzureOpenAI:ApiKey"]) &&
        !string.IsNullOrEmpty(_appSettings["AzureOpenAI:DeploymentName"]);

    public ConfigurationStateService(IConfiguration configuration)
    {
        _appSettings = configuration;
    }

    public void UpdateConfiguration(
        string organizationUrl,
        string personalAccessToken,
        string projectName,
        string? teamName)
    {
        _configuration.OrganizationUrl = organizationUrl;
        _configuration.PersonalAccessToken = personalAccessToken;
        _configuration.ProjectName = projectName;
        _configuration.TeamName = teamName;

        // Reset services so they get recreated with new config
        _devOpsService = null;
        _chatService = null;

        OnConfigurationChanged?.Invoke();
    }

    public void ClearConfiguration()
    {
        _configuration.OrganizationUrl = null;
        _configuration.PersonalAccessToken = null;
        _configuration.ProjectName = null;
        _configuration.TeamName = null;
        _devOpsService = null;
        _chatService = null;
        OnConfigurationChanged?.Invoke();
    }

    public AzureDevOpsService? GetDevOpsService()
    {
        if (!_configuration.IsDevOpsConfigured)
            return null;

        _devOpsService ??= new AzureDevOpsService(
            _configuration.OrganizationUrl!,
            _configuration.PersonalAccessToken!,
            _configuration.ProjectName!,
            _configuration.TeamName);
        _ = _devOpsService.GetCurrentSprintAsync(); // Preload current sprint data
        return _devOpsService;
    }

    public ChatService? GetChatService()
    {
        if (!IsConfigured)
            return null;

        if (_chatService == null)
        {
            var devOpsService = GetDevOpsService();
            if (devOpsService == null)
                return null;

            // Read OpenAI settings from appsettings
            var endpoint = _appSettings["AzureOpenAI:Endpoint"]!;
            var apiKey = _appSettings["AzureOpenAI:ApiKey"]!;
            var deploymentName = _appSettings["AzureOpenAI:DeploymentName"]!;

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            kernelBuilder.Plugins.AddFromObject(new AzureDevOpsPlugin(devOpsService), "AzureDevOps");

            var kernel = kernelBuilder.Build();
            _chatService = new ChatService(kernel);
        }

        return _chatService;
    }
}
