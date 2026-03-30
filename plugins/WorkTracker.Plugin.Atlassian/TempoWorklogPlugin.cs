using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Plugin for uploading worklogs to Tempo (Jira time tracking)
/// </summary>
public sealed class TempoWorklogPlugin : WorklogUploadPluginBase, IDisposable
{
	private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
	private const int MaxRetries = 2;

	private static class ConfigKeys
	{
		public const string TempoBaseUrl = "TempoBaseUrl";
		public const string TempoApiToken = "TempoApiToken";
		public const string JiraAccountId = "JiraAccountId";
	}

	private HttpClient? _tempoHttpClient;
	private JiraClient? _jiraClient;
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
		Tags = ["tempo", "jira", "timetracking", "worklog"]
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			new()
			{
				Key = ConfigKeys.TempoBaseUrl,
				Label = "Tempo API URL",
				Description = "The base URL for Tempo API (e.g., https://api.eu.tempo.io/4)",
				Type = PluginConfigurationFieldType.Url,
				IsRequired = true,
				DefaultValue = "https://api.eu.tempo.io/4",
				Placeholder = "https://api.eu.tempo.io/4",
				ValidationPattern = @"^https://.*",
				ValidationMessage = "Please enter a valid HTTPS URL"
			},
			new()
			{
				Key = ConfigKeys.TempoApiToken,
				Label = "Tempo API Token",
				Description = "Your Tempo API token (get it from Tempo Settings > API Integration)",
				Type = PluginConfigurationFieldType.Password,
				IsRequired = true,
				Placeholder = "Enter your Tempo API token"
			},
			JiraConfigFields.BaseUrlField,
			JiraConfigFields.EmailField,
			JiraConfigFields.ApiTokenField,
			new()
			{
				Key = ConfigKeys.JiraAccountId,
				Label = "Jira Account ID (Optional)",
				Description = "Your Jira account ID. If not provided, it will be automatically retrieved.",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = false,
				Placeholder = "Leave empty to auto-detect"
			}
		];
	}

	protected override async Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_issueIdCache.Clear();

		var tempoBaseUrl = GetRequiredConfigValue(ConfigKeys.TempoBaseUrl).TrimEnd('/') + "/";
		var tempoApiToken = GetRequiredConfigValue(ConfigKeys.TempoApiToken);
		_jiraAccountId = GetConfigValue(ConfigKeys.JiraAccountId);

		// Dispose previous clients and reset disposed state for re-initialization
		_tempoHttpClient?.Dispose();
		_jiraClient?.Dispose();
		_disposed = false;

		// Initialize Tempo HTTP client
		_tempoHttpClient = new HttpClient
		{
			BaseAddress = new Uri(tempoBaseUrl),
			Timeout = HttpTimeout
		};
		_tempoHttpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", tempoApiToken);
		_tempoHttpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));

		// Initialize shared Jira client
		_jiraClient = new JiraClient(
			GetRequiredConfigValue(JiraConfigFields.JiraBaseUrl),
			GetRequiredConfigValue(JiraConfigFields.JiraEmail),
			GetRequiredConfigValue(JiraConfigFields.JiraApiToken));

		// Get account ID if not provided
		if (string.IsNullOrWhiteSpace(_jiraAccountId))
		{
			_jiraAccountId = await _jiraClient.GetCurrentUserAccountIdAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(_jiraAccountId))
			{
				Logger?.LogError("Failed to retrieve Jira account ID");
				return false;
			}
		}

		return true;
	}

	protected override Task OnShutdownAsync()
	{
		_issueIdCache.Clear();
		Dispose();
		return Task.CompletedTask;
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken)
	{
		if (_jiraClient == null || _tempoHttpClient == null)
		{
			return PluginResult<bool>.Failure("Plugin is not properly initialized");
		}

		var (success, error) = await _jiraClient.TestConnectionAsync(cancellationToken);
		return success
			? PluginResult<bool>.Success(true)
			: PluginResult<bool>.Failure(error!);
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
			var issueId = await GetIssueIdFromKeyAsync(worklog.TicketId, cancellationToken);
			if (!issueId.HasValue)
			{
				return PluginResult<bool>.Failure($"Could not resolve issue ID for {worklog.TicketId}");
			}

			var timeSpentSeconds = worklog.DurationMinutes * 60;
			if (timeSpentSeconds <= 0)
			{
				return PluginResult<bool>.Failure("Invalid duration: must be greater than 0");
			}

			var tempoWorklog = new
			{
				issueId = issueId.Value,
				timeSpentSeconds,
				startDate = worklog.StartTime.ToString("yyyy-MM-dd"),
				startTime = worklog.StartTime.ToString("HH:mm:ss"),
				authorAccountId = _jiraAccountId,
				description = worklog.Description ?? string.Empty
			};

			string? lastError = null;
			for (var attempt = 0; attempt <= MaxRetries; attempt++)
			{
				using var response = await _tempoHttpClient!.PostAsJsonAsync("worklogs", tempoWorklog, cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					Logger?.LogInformation("Successfully uploaded worklog for {Ticket} ({Duration} minutes)",
						worklog.TicketId, worklog.DurationMinutes);
					return PluginResult<bool>.Success(true);
				}

				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var statusCode = (int)response.StatusCode;
				lastError = $"Upload failed: {response.StatusCode} - {errorContent}";

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
					$"Failed to get worklogs: {response.StatusCode} - {errorContent}");
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
					var description = item.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

					if (DateTime.TryParse($"{startDateStr} {startTimeStr}",
						System.Globalization.CultureInfo.InvariantCulture,
						System.Globalization.DateTimeStyles.None, out var startTime))
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
		var result = await GetWorklogsAsync(worklog.StartTime.Date, worklog.StartTime.Date, cancellationToken);

		if (result.IsFailure)
		{
			return PluginResult<bool>.Failure(result.Error!);
		}

		var exists = result.Value!.Any(w =>
			w.TicketId == worklog.TicketId &&
			w.StartTime.Date == worklog.StartTime.Date &&
			Math.Abs((w.StartTime.TimeOfDay - worklog.StartTime.TimeOfDay).TotalMinutes) < 1 &&
			w.DurationMinutes == worklog.DurationMinutes);

		return PluginResult<bool>.Success(exists);
	}

	private async Task<int?> GetIssueIdFromKeyAsync(string issueKey, CancellationToken cancellationToken)
	{
		if (_issueIdCache.TryGetValue(issueKey, out var cached) &&
			DateTime.UtcNow - cached.CachedAt < CacheTtl)
		{
			return cached.Id;
		}

		try
		{
			var json = await _jiraClient!.GetJsonAsync($"/rest/api/3/issue/{issueKey}?fields=id", cancellationToken);

			if (json.TryGetProperty("id", out var idElement) &&
				int.TryParse(idElement.GetString(), out var id))
			{
				_issueIdCache[issueKey] = (id, DateTime.UtcNow);
				return id;
			}

			Logger?.LogWarning("Could not parse issue ID from response for {IssueKey}", issueKey);
			return null;
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Failed to fetch issue ID for {IssueKey}", issueKey);
			return null;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_tempoHttpClient?.Dispose();
			_tempoHttpClient = null;
			_jiraClient?.Dispose();
			_jiraClient = null;
			_issueIdCache.Clear();
			_disposed = true;
		}
	}
}
