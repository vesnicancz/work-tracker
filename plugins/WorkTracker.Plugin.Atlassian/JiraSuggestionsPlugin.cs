using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Plugin that suggests work items based on Jira issues matching a JQL filter
/// </summary>
public sealed class JiraSuggestionsPlugin(IHttpClientFactory httpClientFactory, ILogger<JiraSuggestionsPlugin> logger)
	: WorkSuggestionPluginBase(logger)
{
	private static class ConfigKeys
	{
		public const string JqlFilter = "JqlFilter";
		public const string SearchJqlFilter = "SearchJqlFilter";
		public const string MaxResults = "MaxResults";
	}

	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

	private IJiraClient? _jiraClient;
	private string? _jqlFilter;
	private string? _searchJqlFilter;
	private int _maxResults;

	public override PluginMetadata Metadata => new()
	{
		Id = "jira.suggestions",
		Name = "Jira Suggestions",
		Version = new Version(1, 1, 0),
		Author = "WorkTracker Team",
		Description = "Suggests work items based on Jira issues matching a JQL filter",
		Website = "https://www.atlassian.com/software/jira",
		IconName = "Jira",
		Tags = ["jira", "suggestions", "issues"]
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			JiraConfigFields.BaseUrlField,
			JiraConfigFields.EmailField,
			JiraConfigFields.ApiTokenField,
			new()
			{
				Key = ConfigKeys.JqlFilter,
				Label = "JQL Filter",
				Description = "JQL query for daily suggestions. Also used as the base filter for search when Search JQL Filter is not configured.",
				Type = PluginConfigurationFieldType.MultilineText,
				IsRequired = false,
				DefaultValue = "assignee = currentUser() AND status != Done ORDER BY updated DESC",
				Placeholder = "assignee = currentUser() AND status != Done ORDER BY updated DESC"
			},
			new()
			{
				Key = ConfigKeys.SearchJqlFilter,
				Label = "Search JQL Filter",
				Description = "JQL template for search. Use {query} as a placeholder for the search text, and place {query} inside double quotes when used with ~ (e.g., summary ~ \"{query}\"). The value for {query} is escaped for use inside a quoted JQL string (backslashes/quotes escaped, * and newlines removed). If {query} is not present, the filter is combined with a default text search. If not set, JQL Filter is used instead.",
				Type = PluginConfigurationFieldType.MultilineText,
				IsRequired = false,
				Placeholder = "project = PROJ AND (summary ~ \"{query}*\" OR key ~ \"{query}*\") ORDER BY updated DESC"
			},
			new()
			{
				Key = ConfigKeys.MaxResults,
				Label = "Max Results",
				Description = "Maximum number of issues to return",
				Type = PluginConfigurationFieldType.Number,
				IsRequired = false,
				DefaultValue = "20",
				ValidationPattern = @"^\d+$",
				ValidationMessage = "Please enter a valid number"
			}
		];
	}

	protected override Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_jqlFilter = GetConfigValue(ConfigKeys.JqlFilter)
			?? "assignee = currentUser() AND status != Done ORDER BY updated DESC";
		var searchJqlConfig = GetConfigValue(ConfigKeys.SearchJqlFilter);
		_searchJqlFilter = string.IsNullOrWhiteSpace(searchJqlConfig) ? null : searchJqlConfig;
		_maxResults = int.TryParse(GetConfigValue(ConfigKeys.MaxResults), out var max) ? max : 20;

		var newClient = JiraClient.Create(
			_httpClientFactory,
			GetRequiredConfigValue(JiraConfigFields.JiraBaseUrl),
			GetRequiredConfigValue(JiraConfigFields.JiraEmail),
			GetRequiredConfigValue(JiraConfigFields.JiraApiToken));

		var oldClient = _jiraClient;
		_jiraClient = newClient;
		oldClient?.Dispose();

		return Task.FromResult(true);
	}

	public override ValueTask DisposeAsync()
	{
		_jiraClient?.Dispose();
		GC.SuppressFinalize(this);
		return ValueTask.CompletedTask;
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		var (success, error) = await _jiraClient!.TestConnectionAsync(cancellationToken);
		return success
			? PluginResult<bool>.Success(true)
			: PluginResult<bool>.Failure(error!, PluginErrorCategory.Authentication);
	}

	public override bool SupportsSearch => true;

	public override Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(DateTime date, CancellationToken cancellationToken)
	{
		return ExecuteJqlSearchAsync(_jqlFilter!, _maxResults, cancellationToken);
	}

	public override Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(
		string query, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		string jql;
		if (_searchJqlFilter is not null && _searchJqlFilter.Contains("{query}"))
		{
			jql = ApplySearchTemplate(query, _searchJqlFilter);
		}
		else
		{
			jql = BuildSearchJql(query, _searchJqlFilter ?? _jqlFilter!);
		}

		return ExecuteJqlSearchAsync(jql, _maxResults, cancellationToken);
	}

	internal static string EscapeJqlQueryText(string query)
	{
		return query
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("*", "")
			.Replace("\r", "")
			.Replace("\n", "");
	}

	internal static string BuildSearchJql(string query, string baseJqlFilter)
	{
		var escapedQuery = EscapeJqlQueryText(query);
		var textFilter = $"(key ~ \"{escapedQuery}*\" OR summary ~ \"{escapedQuery}*\" OR text ~ \"{escapedQuery}*\")";

		var orderByIndex = baseJqlFilter.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
		var filterPart = orderByIndex >= 0 ? baseJqlFilter[..orderByIndex].Trim() : baseJqlFilter.Trim();
		var orderPart = orderByIndex >= 0 ? baseJqlFilter[orderByIndex..] : "ORDER BY updated DESC";

		return string.IsNullOrEmpty(filterPart)
			? $"{textFilter} {orderPart}"
			: $"({filterPart}) AND {textFilter} {orderPart}";
	}

	internal static string ApplySearchTemplate(string query, string jqlTemplate)
	{
		var escapedQuery = EscapeJqlQueryText(query);
		return jqlTemplate.Replace("{query}", escapedQuery);
	}

	private async Task<PluginResult<IReadOnlyList<WorkSuggestion>>> ExecuteJqlSearchAsync(string jql, int maxResults, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		try
		{
			var encodedJql = Uri.EscapeDataString(jql);
			var url = $"/rest/api/3/search/jql?jql={encodedJql}&maxResults={maxResults}&fields=summary,status,issuetype,priority";

			using var response = await _jiraClient!.GetAsync(url, cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
				Logger.LogWarning("Jira search failed with {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
				return PluginResult<IReadOnlyList<WorkSuggestion>>.Failure(
					$"Jira search failed ({(int)response.StatusCode}): {errorBody}", PluginErrorCategory.Network);
			}

			var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
			var suggestions = new List<WorkSuggestion>();

			if (json.TryGetProperty("issues", out var issues))
			{
				foreach (var issue in issues.EnumerateArray())
				{
					var key = issue.GetProperty("key").GetString()!;
					var fields = issue.GetProperty("fields");
					var summary = fields.GetProperty("summary").GetString() ?? key;

					suggestions.Add(new WorkSuggestion
					{
						Title = summary,
						TicketId = key,
						Source = "Jira",
						SourceId = key,
						SourceUrl = $"{_jiraClient.BaseUrl}/browse/{key}"
					});
				}
			}

			Logger.LogInformation("Fetched {Count} Jira suggestions for JQL: {Jql}", suggestions.Count, jql);
			return PluginResult<IReadOnlyList<WorkSuggestion>>.Success(suggestions);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Failed to fetch Jira suggestions");
			return PluginResult<IReadOnlyList<WorkSuggestion>>.Failure($"Failed to fetch suggestions: {ex.Message}");
		}
	}

}
