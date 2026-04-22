using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Atlassian;

/// <summary>
/// Plugin for uploading worklogs to Tempo (Jira time tracking)
/// </summary>
public sealed class TempoWorklogPlugin(IHttpClientFactory httpClientFactory, ILogger<TempoWorklogPlugin> logger)
	: WorklogUploadPluginBase(logger)
{
	private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
	private const int MaxRetries = 2;

	private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
	[
		HttpStatusCode.RequestTimeout,       // 408
		HttpStatusCode.TooManyRequests,       // 429
		HttpStatusCode.InternalServerError,   // 500
		HttpStatusCode.BadGateway,            // 502
		HttpStatusCode.ServiceUnavailable,    // 503
		HttpStatusCode.GatewayTimeout         // 504
	];

	private static class ConfigKeys
	{
		public const string TempoBaseUrl = "TempoBaseUrl";
		public const string TempoApiToken = "TempoApiToken";
		public const string JiraAccountId = "JiraAccountId";
	}

	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

	private HttpClient? _tempoHttpClient;
	private IJiraClient? _jiraClient;
	private string? _jiraAccountId;
	internal Func<int, TimeSpan>? RetryDelayStrategy { get; set; }

	private readonly ConcurrentDictionary<string, (int Id, DateTime CachedAt)> _issueIdCache = new();

	public override PluginMetadata Metadata => new()
	{
		Id = "tempo.worklog",
		Name = "Tempo Timesheets",
		Version = new Version(1, 1, 0),
		Author = "WorkTracker Team",
		Description = "Upload worklogs to Tempo (Jira time tracking system)",
		Website = "https://www.tempo.io/",
		Tags = ["tempo", "jira", "timetracking", "worklog"]
	};

	public override WorklogSubmissionMode SupportedModes =>
		WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated;

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
		var tempoBaseUrl = GetRequiredConfigValue(ConfigKeys.TempoBaseUrl).TrimEnd('/') + "/";
		var tempoApiToken = GetRequiredConfigValue(ConfigKeys.TempoApiToken);
		var jiraAccountId = GetConfigValue(ConfigKeys.JiraAccountId);

		// Create new clients before disposing old ones — if creation fails, the old state stays valid
		var newTempoClient = CreateTempoHttpClient(tempoBaseUrl, tempoApiToken);
		var newJiraClient = JiraClient.Create(
			_httpClientFactory,
			GetRequiredConfigValue(JiraConfigFields.JiraBaseUrl),
			GetRequiredConfigValue(JiraConfigFields.JiraEmail),
			GetRequiredConfigValue(JiraConfigFields.JiraApiToken));

		// Validate account ID before committing to the new clients
		if (string.IsNullOrWhiteSpace(jiraAccountId))
		{
			try
			{
				jiraAccountId = await newJiraClient.GetCurrentUserAccountIdAsync(cancellationToken);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to retrieve Jira account ID");
			}

			if (string.IsNullOrWhiteSpace(jiraAccountId))
			{
				Logger.LogError("Jira account ID auto-detection returned null or empty");
				newTempoClient.Dispose();
				newJiraClient.Dispose();
				return false;
			}
		}

		_jiraAccountId = jiraAccountId;

		// Success — swap clients, clear cache, and dispose old ones
		_issueIdCache.Clear();
		var oldTempoClient = _tempoHttpClient;
		var oldJiraClient = _jiraClient;
		_tempoHttpClient = newTempoClient;
		_jiraClient = newJiraClient;
		oldTempoClient?.Dispose();
		oldJiraClient?.Dispose();

		return true;
	}

	private HttpClient CreateTempoHttpClient(string baseUrl, string apiToken)
	{
		var client = _httpClientFactory.CreateClient();
		client.BaseAddress = new Uri(baseUrl);
		client.Timeout = TimeSpan.FromSeconds(30);
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
		client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		return client;
	}

	protected override Task OnShutdownAsync()
	{
		_issueIdCache.Clear();
		return Task.CompletedTask;
	}

	protected override ValueTask OnDisposeAsync()
	{
		_tempoHttpClient?.Dispose();
		_jiraClient?.Dispose();

		return ValueTask.CompletedTask;
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		var (success, error, statusCode) = await _jiraClient!.TestConnectionAsync(cancellationToken);
		if (success)
		{
			return PluginResult<bool>.Success(true);
		}

		var category = statusCode is 401 or 403
			? PluginErrorCategory.Authentication
			: PluginErrorCategory.Network;
		return PluginResult<bool>.Failure(error!, category);
	}

	public override Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
		=> PostWorklogAsync(worklog, includeStartTime: true, cancellationToken);

	public override async Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(
		IEnumerable<PluginWorklogEntry> worklogs,
		WorklogSubmissionMode mode,
		CancellationToken cancellationToken)
	{
		// In Aggregated mode Tempo gets startDate + timeSpentSeconds only (no startTime),
		// since the aggregated row's start is just a representative timestamp, not a real interval.
		// Timed mode keeps the per-entry startTime so Tempo reports retain the chronological layout.
		if (!mode.IsSingleMode())
		{
			return PluginResult<WorklogSubmissionResult>.Failure(
				$"Invalid submission mode '{mode}' — must be exactly Timed or Aggregated",
				PluginErrorCategory.Validation);
		}

		if (!SupportedModes.HasFlag(mode))
		{
			return PluginResult<WorklogSubmissionResult>.Failure(
				$"Tempo does not support submission mode '{mode}'",
				PluginErrorCategory.Validation);
		}

		if (!IsInitialized)
		{
			return PluginResult<WorklogSubmissionResult>.Failure(
				"Plugin is not initialized",
				PluginErrorCategory.Validation);
		}

		var includeStartTime = mode != WorklogSubmissionMode.Aggregated;
		var worklogList = worklogs.ToList();
		var successful = 0;
		var failed = 0;
		var errors = new List<WorklogSubmissionError>();

		foreach (var worklog in worklogList)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var result = await PostWorklogAsync(worklog, includeStartTime, cancellationToken);
			if (result.IsSuccess)
			{
				successful++;
			}
			else
			{
				failed++;
				errors.Add(new WorklogSubmissionError
				{
					Worklog = worklog,
					ErrorMessage = result.Error ?? "Unknown error"
				});
			}
		}

		return PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
		{
			TotalEntries = worklogList.Count,
			SuccessfulEntries = successful,
			FailedEntries = failed,
			Errors = errors
		});
	}

	private async Task<PluginResult<bool>> PostWorklogAsync(PluginWorklogEntry worklog, bool includeStartTime, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		if (string.IsNullOrWhiteSpace(worklog.TicketId))
		{
			return PluginResult<bool>.Failure("Ticket ID is required for Tempo upload", PluginErrorCategory.Validation);
		}

		try
		{
			var issueId = await GetIssueIdFromKeyAsync(worklog.TicketId, cancellationToken);
			if (!issueId.HasValue)
			{
				return PluginResult<bool>.Failure($"Could not resolve issue ID for {worklog.TicketId}", PluginErrorCategory.NotFound);
			}

			var timeSpentSeconds = worklog.DurationMinutes * 60;
			if (timeSpentSeconds <= 0)
			{
				return PluginResult<bool>.Failure("Invalid duration: must be greater than 0", PluginErrorCategory.Validation);
			}

			var startDate = worklog.StartTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
			var startTime = includeStartTime
				? worklog.StartTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
				: null;

			// Use different payload shapes per mode: Timed includes startTime, Aggregated omits
			// the property entirely (different anonymous type → different JSON contract).
			object tempoWorklog = includeStartTime
				? new
				{
					issueId = issueId.Value,
					timeSpentSeconds,
					startDate,
					startTime,
					authorAccountId = _jiraAccountId,
					description = worklog.Description ?? string.Empty
				}
				: new
				{
					issueId = issueId.Value,
					timeSpentSeconds,
					startDate,
					authorAccountId = _jiraAccountId,
					description = worklog.Description ?? string.Empty
				};

			string? lastError = null;
			for (var attempt = 0; attempt <= MaxRetries; attempt++)
			{
				using var response = await _tempoHttpClient!.PostAsJsonAsync("worklogs", tempoWorklog, cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					Logger.LogInformation("Successfully uploaded worklog for {Ticket} ({Duration} minutes)",
						worklog.TicketId, worklog.DurationMinutes);
					return PluginResult<bool>.Success(true);
				}

				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				lastError = $"Upload failed: {response.StatusCode} - {errorContent}";

				if (attempt < MaxRetries && RetryableStatusCodes.Contains(response.StatusCode))
				{
					var delay = RetryDelayStrategy?.Invoke(attempt)
						?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
					Logger.LogWarning("Tempo API returned {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{MaxAttempts})",
						response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries + 1);
					await Task.Delay(delay, cancellationToken);
					continue;
				}

				break;
			}

			return PluginResult<bool>.Failure(lastError ?? "Upload failed", PluginErrorCategory.Network);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Error uploading worklog to Tempo");
			return PluginResult<bool>.Failure($"Error: {ex.Message}");
		}
	}

	public override async Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		try
		{
			var from = startDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
			var to = endDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

			using var response = await _tempoHttpClient!.GetAsync($"worklogs?from={from}&to={to}", cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				return PluginResult<IEnumerable<PluginWorklogEntry>>.Failure(
					$"Failed to get worklogs: {response.StatusCode} - {errorContent}", PluginErrorCategory.Network);
			}

			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			using var jsonDoc = JsonDocument.Parse(content);
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
					if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(startTimeStr))
					{
						continue;
					}

					var timeSpentSeconds = item.TryGetProperty("timeSpentSeconds", out var tsProp) ? tsProp.GetInt32() : 0;
					var description = item.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

					if (DateTime.TryParseExact($"{startDateStr} {startTimeStr}",
						"yyyy-MM-dd HH:mm:ss",
						System.Globalization.CultureInfo.InvariantCulture,
						System.Globalization.DateTimeStyles.None, out var parsedStartTime))
					{
						worklogs.Add(new PluginWorklogEntry
						{
							TicketId = ticketId,
							Description = description,
							StartTime = parsedStartTime,
							EndTime = parsedStartTime.AddSeconds(timeSpentSeconds),
							DurationMinutes = timeSpentSeconds / 60
						});
					}
				}
				catch (Exception ex)
				{
					Logger.LogWarning(ex, "Failed to parse worklog entry from Tempo API response");
				}
			}

			return PluginResult<IEnumerable<PluginWorklogEntry>>.Success(worklogs);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Error getting worklogs from Tempo");
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
		if (_issueIdCache.TryGetValue(issueKey, out var cached))
		{
			if (DateTime.UtcNow - cached.CachedAt < CacheTtl)
			{
				return cached.Id;
			}

			_issueIdCache.TryRemove(issueKey, out _);
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

			Logger.LogWarning("Could not parse issue ID from response for {IssueKey}", issueKey);
			return null;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Failed to fetch issue ID for {IssueKey}", issueKey);
			return null;
		}
	}

}
