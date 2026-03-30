using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Shared Jira configuration field definitions and keys.
/// </summary>
internal static class JiraConfigFields
{
	public const string JiraBaseUrl = "JiraBaseUrl";
	public const string JiraEmail = "JiraEmail";
	public const string JiraApiToken = "JiraApiToken";

	public static PluginConfigurationField BaseUrlField { get; } = new()
	{
		Key = JiraBaseUrl,
		Label = "Jira Base URL",
		Description = "Your Jira instance URL (e.g., https://your-domain.atlassian.net)",
		Type = PluginConfigurationFieldType.Url,
		IsRequired = true,
		Placeholder = "https://your-domain.atlassian.net",
		ValidationPattern = @"^https://.*",
		ValidationMessage = "Please enter a valid HTTPS URL"
	};

	public static PluginConfigurationField EmailField { get; } = new()
	{
		Key = JiraEmail,
		Label = "Jira Email",
		Description = "Your Jira account email address",
		Type = PluginConfigurationFieldType.Email,
		IsRequired = true,
		Placeholder = "user@example.com"
	};

	public static PluginConfigurationField ApiTokenField { get; } = new()
	{
		Key = JiraApiToken,
		Label = "Jira API Token",
		Description = "Your Jira API token (get it from Atlassian Account Settings > Security > API tokens)",
		Type = PluginConfigurationFieldType.Password,
		IsRequired = true,
		Placeholder = "Enter your Jira API token"
	};
}