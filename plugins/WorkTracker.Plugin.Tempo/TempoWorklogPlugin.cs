using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Tempo;

/// <summary>
/// Plugin for uploading worklogs to Tempo (Jira time tracking)
/// </summary>
public sealed class TempoWorklogPlugin : WorklogUploadPluginBase, IDisposable
{
	private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
	private const int MaxRetries = 2;

	private HttpClient? _tempoHttpClient;
	private HttpClient? _jiraHttpClient;
	private string? _tempoBaseUrl;
	private string? _tempoApiToken;
	private string? _jiraBaseUrl;
	private string? _jiraEmail;
	private string? _jiraApiToken;
	private string? _jiraAccountId;
	private bool _disposed;

	private readonly ConcurrentDictionary<string, (int Id, DateTime CachedAt)> _issueIdCache = new();

	public override PluginMetadata Metadata => new()
	{
		Id = "tempo.worklog",
		Name = "Tempo Timesheets",
		Version = new Version(1, 0, 0),
		Author = "WorkTracker Team",
		Description = "Upload worklogs to Tempo (Jira time tracking system)",
		Website = "https://www.tempo.io/",
		Tags = new[] { "tempo", "jira", "timetracking", "worklog" }
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return new List<PluginConfigurationField>
		{
			new()
			{
				Key = "TempoBaseUrl",
				Label = "Tempo API URL",
				Description = "The base URL for Tempo API (e.g., https://api.eu.tempo.io/4)",
				Type = PluginConfigurationFieldType.Url,
				IsRequired = true,
				DefaultValue = "https://api.eu.tempo.io/4",
				Placeholder = "https://api.eu.tempo.io/4",
				ValidationPattern = @"^https?://.*",
				ValidationMessage = "Please enter a valid URL starting with http:// or https://"
			},
			new()
			{
				Key = "TempoApiToken",
				Label = "Tempo API Token",
				Description = "Your Tempo API token (get it from Tempo Settings > API Integration)",
				Type = PluginConfigurationFieldType.Password,
				IsRequired = true,
				Placeholder = "Enter your Tempo API token"
			},
			new()
			{
				Key = "JiraBaseUrl",
				Label = "Jira Base URL",
				Description = "Your Jira instance URL (e.g., https://your-domain.atlassian.net)",
				Type = PluginConfigurationFieldType.Url,
				IsRequired = true,
				Placeholder = "https://your-domain.atlassian.net",
				ValidationPattern = @"^https?://.*",
				ValidationMessage = "Please enter a valid URL starting with http:// or https://"
			},
			new()
			{
				Key = "JiraEmail",
				Label = "Jira Email",
				Description = "Your Jira account email address",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "user@example.com"
			},
			new()
			{
				Key = "JiraApiToken",
				Label = "Jira API Token",
				Description = "Your Jira API token (get it from Atlassian Account Settings > Security > API tokens)",
				Type = PluginConfigurationFieldType.Password,
				IsRequired = true,
				Placeholder = "Enter your Jira API token"
			},
			new()
			{
				Key = "JiraAccountId",
				Label = "Jira Account ID (Optional)",
				Description = "Your Jira account ID. If not provided, it will be automatically retrieved.",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = false,
				Placeholder = "Leave empty to auto-detect"
			}
		};
	}

	protected override async Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		// Clear cache on re-initialization
		_issueIdCache.Clear();

		// Load Tempo configuration
		_tempoBaseUrl = GetRequiredConfigValue("TempoBaseUrl").TrimEnd('/') + "/"; // Add trailing slash for proper URL resolution
		_tempoApiToken = GetRequiredConfigValue("TempoApiToken");

		// Load Jira configuration
		_jiraBaseUrl = GetRequiredConfigValue("JiraBaseUrl").TrimEnd('/');
		_jiraEmail = GetRequiredConfigValue("JiraEmail");
		_jiraApiToken = GetRequiredConfigValue("JiraApiToken");
		_jiraAccountId = GetConfigValue("JiraAccountId");

		// Initialize Tempo HTTP client
		_tempoHttpClient = new HttpClient
		{
			BaseAddress = new Uri(_tempoBaseUrl),
			Timeout = HttpTimeout
		};
		_tempoHttpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", _tempoApiToken);
		_tempoHttpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));

		// Initialize Jira HTTP client with Basic authentication
		_jiraHttpClient = new HttpClient
		{
			BaseAddress = new Uri(_jiraBaseUrl),
			Timeout = HttpTimeout
		};
		var authString = $"{_jiraEmail}:{_jiraApiToken}";
		var base64Auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(authString));
		_jiraHttpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Basic", base64Auth);
		_jiraHttpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));

		// Get account ID if not provided
		if (string.IsNullOrWhiteSpace(_jiraAccountId))
		{
			_jiraAccountId = await GetCurrentUserAccountIdAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(_jiraAccountId))
			{
				Logger?.LogError("Failed to retrieve Jira account ID");
				return false;
			}
		}

		Logger?.LogInformation("Plugin initialized successfully");
		return true;
	}

	protected override async Task OnShutdownAsync()
	{
		_issueIdCache.Clear();
		Dispose();
		await Task.CompletedTask;
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken)
	{
		// Check if HTTP clients are initialized (can be called during initialization)
		if (_jiraHttpClient == null || _tempoHttpClient == null)
		{
			return PluginResult<bool>.Failure("Plugin is not properly initialized");
		}

		try
		{
			// Test Jira connection (required for issue key to ID translation)
			var jiraResponse = await _jiraHttpClient.GetAsync("/rest/api/3/myself", cancellationToken);
			if (!jiraResponse.IsSuccessStatusCode)
			{
				var errorContent = await jiraResponse.Content.ReadAsStringAsync(cancellationToken);
				Logger?.LogWarning("Jira connection test failed: {StatusCode} - {Content}",
					jiraResponse.StatusCode, errorContent);
				return PluginResult<bool>.Failure($"Jira connection failed: {jiraResponse.StatusCode}");
			}

			Logger?.LogInformation("Jira and Tempo configuration test successful");
			return PluginResult<bool>.Success(true);
		}
		catch (HttpRequestException ex)
		{
			Logger?.LogError(ex, "Connection test failed with HTTP error");
			return PluginResult<bool>.Failure($"Connection error: {ex.Message}");
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Connection test failed");
			return PluginResult<bool>.Failure($"Unexpected error: {ex.Message}");
		}
	}

	public override async Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		if (string.IsNullOrWhiteSpace(worklog.TicketId))
		{
			return PluginResult<bool>.Failure("Ticket ID is required for Tempo upload");
		}

		try
		{
			// Convert issue key to issue ID (uses cache)
			var issueId = await GetIssueIdFromKeyAsync(worklog.TicketId, cancellationToken);
			if (!issueId.HasValue)
			{
				Logger?.LogError("Could not resolve issue ID for {TicketId}", worklog.TicketId);
				return PluginResult<bool>.Failure($"Could not resolve issue ID for {worklog.TicketId}");
			}

			// Calculate duration in seconds
			var timeSpentSeconds = worklog.DurationMinutes * 60;
			if (timeSpentSeconds <= 0)
			{
				Logger?.LogWarning("Invalid duration for {TicketId}", worklog.TicketId);
				return PluginResult<bool>.Failure("Invalid duration: must be greater than 0");
			}

			// Create Tempo worklog with correct format
			var tempoWorklog = new
			{
				issueId = issueId.Value,
				timeSpentSeconds = timeSpentSeconds,
				startDate = worklog.StartTime.ToString("yyyy-MM-dd"),
				startTime = worklog.StartTime.ToString("HH:mm:ss"),
				authorAccountId = _jiraAccountId,
				description = worklog.Description ?? string.Empty
			};

			Logger?.LogDebug(
				"Submitting worklog to Tempo: {TicketId} (ID: {IssueId}), Duration: {Duration}s",
				worklog.TicketId, issueId.Value, timeSpentSeconds
			);

			// Retry on transient failures
			string? lastError = null;
			for (var attempt = 0; attempt <= MaxRetries; attempt++)
			{
				using var response = await _tempoHttpClient!.PostAsJsonAsync("worklogs", tempoWorklog, cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					Logger?.LogInformation(
						"Successfully uploaded worklog for {Ticket} ({Duration} minutes)",
						worklog.TicketId,
						worklog.DurationMinutes
					);
					return PluginResult<bool>.Success(true);
				}

				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var statusCode = (int)response.StatusCode;
				lastError = $"Upload failed: {response.StatusCode} - {errorContent}";

				// Only retry on server errors (5xx) or rate limiting (429)
				if (attempt < MaxRetries && (statusCode >= 500 || statusCode == 429))
				{
					var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
					Logger?.LogWarning("Tempo API returned {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
						response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
					await Task.Delay(delay, cancellationToken);
					continue;
				}

				break;
			}

			Logger?.LogWarning("Failed to upload worklog: {Error}", lastError);
			return PluginResult<bool>.Failure(lastError ?? "Upload failed");
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error uploading worklog to Tempo");
			return PluginResult<bool>.Failure($"Error: {ex.Message}");
		}
	}

	public override async Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		try
		{
			var from = startDate.ToString("yyyy-MM-dd");
			var to = endDate.ToString("yyyy-MM-dd");

			var response = await _tempoHttpClient!.GetAsync($"worklogs?from={from}&to={to}", cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				return PluginResult<IEnumerable<PluginWorklogEntry>>.Failure(
					$"Failed to get worklogs: {response.StatusCode} - {errorContent}"
				);
			}

			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			var jsonDoc = JsonDocument.Parse(content);
			var results = jsonDoc.RootElement.GetProperty("results");

			var worklogs = new List<PluginWorklogEntry>();

			foreach (var item in results.EnumerateArray())
			{
				try
				{
					if (!item.TryGetProperty("issue", out var issueProp) ||
						!issueProp.TryGetProperty("key", out var keyProp))
					{
						continue;
					}

					var ticketId = keyProp.GetString();
					if (string.IsNullOrEmpty(ticketId))
					{
						continue;
					}

					var startDateStr = item.TryGetProperty("startDate", out var sdProp) ? sdProp.GetString() : null;
					var startTimeStr = item.TryGetProperty("startTime", out var stProp) ? stProp.GetString() : null;
					var timeSpentSeconds = item.TryGetProperty("timeSpentSeconds", out var tsProp) ? tsProp.GetInt32() : 0;
					var description = item.TryGetProperty("description", out var descProp)
						? descProp.GetString()
						: null;

					if (DateTime.TryParse($"{startDateStr} {startTimeStr}", out var startTime))
					{
						worklogs.Add(new PluginWorklogEntry
						{
							TicketId = ticketId,
							Description = description,
							StartTime = startTime,
							EndTime = startTime.AddSeconds(timeSpentSeconds),
							DurationMinutes = timeSpentSeconds / 60
						});
					}
				}
				catch (Exception ex)
				{
					Logger?.LogWarning(ex, "Failed to parse worklog entry from Tempo API response");
				}
			}

			return PluginResult<IEnumerable<PluginWorklogEntry>>.Success(worklogs);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error getting worklogs from Tempo");
			return PluginResult<IEnumerable<PluginWorklogEntry>>.Failure($"Error: {ex.Message}");
		}
	}

	public override async Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		var result = await GetWorklogsAsync(
			worklog.StartTime.Date,
			worklog.StartTime.Date,
			cancellationToken
		);

		if (result.IsFailure)
		{
			return PluginResult<bool>.Failure(result.Error!);
		}

		var exists = result.Value!.Any(w =>
			w.TicketId == worklog.TicketId &&
			w.StartTime.Date == worklog.StartTime.Date &&
			Math.Abs((w.StartTime.TimeOfDay - worklog.StartTime.TimeOfDay).TotalMinutes) < 1 &&
			w.DurationMinutes == worklog.DurationMinutes
		);

		return PluginResult<bool>.Success(exists);
	}

	/// <summary>
	/// Converts a Jira issue key (e.g., "PROJ-123") to its numeric issue ID.
	/// Results are cached to avoid redundant HTTP requests.
	/// </summary>
	private async Task<int?> GetIssueIdFromKeyAsync(string issueKey, CancellationToken cancellationToken)
	{
		// Check cache first (with TTL)
		if (_issueIdCache.TryGetValue(issueKey, out var cached) &&
			DateTime.UtcNow - cached.CachedAt < CacheTtl)
		{
			Logger?.LogDebug("Issue ID for {IssueKey} found in cache: {IssueId}", issueKey, cached.Id);
			return cached.Id;
		}

		try
		{
			var url = $"/rest/api/3/issue/{issueKey}?fields=id";
			Logger?.LogDebug("Fetching issue ID for key: {IssueKey}", issueKey);

			var response = await _jiraHttpClient!.GetAsync(url, cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				Logger?.LogError("Failed to get issue {IssueKey}. Status: {Status}, Error: {Error}",
					issueKey, response.StatusCode, errorContent);
				return null;
			}

			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			using var doc = JsonDocument.Parse(content);

			if (doc.RootElement.TryGetProperty("id", out var idElement))
			{
				var idString = idElement.GetString();
				if (int.TryParse(idString, out var id))
				{
					// Cache the result with timestamp
					_issueIdCache[issueKey] = (id, DateTime.UtcNow);
					Logger?.LogDebug("Issue {IssueKey} has ID: {IssueId} (cached)", issueKey, id);
					return id;
				}
			}

			Logger?.LogWarning("Could not parse issue ID from response for {IssueKey}", issueKey);
			return null;
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Exception occurred while fetching issue ID for {IssueKey}", issueKey);
			return null;
		}
	}

	/// <summary>
	/// Gets the current user's Jira account ID
	/// </summary>
	private async Task<string?> GetCurrentUserAccountIdAsync(CancellationToken cancellationToken)
	{
		try
		{
			var url = "/rest/api/3/myself";
			Logger?.LogDebug("Fetching current user account ID");

			var response = await _jiraHttpClient!.GetAsync(url, cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				Logger?.LogError("Failed to get current user. Status: {Status}, Error: {Error}",
					response.StatusCode, errorContent);
				return null;
			}

			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			using var doc = JsonDocument.Parse(content);

			if (doc.RootElement.TryGetProperty("accountId", out var accountIdElement))
			{
				var accountId = accountIdElement.GetString();
				Logger?.LogDebug("Current user account ID: {AccountId}", accountId);
				return accountId;
			}

			Logger?.LogWarning("Could not parse account ID from response");
			return null;
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Exception occurred while fetching current user account ID");
			return null;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_tempoHttpClient?.Dispose();
			_tempoHttpClient = null;
			_jiraHttpClient?.Dispose();
			_jiraHttpClient = null;
			_issueIdCache.Clear();
			_disposed = true;
		}
	}
}