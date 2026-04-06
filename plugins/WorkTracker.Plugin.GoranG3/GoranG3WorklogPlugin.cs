using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3;

/// <summary>
/// Plugin for uploading and reading worklogs via Goran G3 MCP server.
/// </summary>
public sealed class GoranG3WorklogPlugin(ILogger<GoranG3WorklogPlugin> logger, ITokenProviderFactory tokenProviderFactory)
	: WorklogUploadPluginBase(logger)
{
	private static class ConfigKeys
	{
		public const string GoranBaseUrl = "GoranBaseUrl";
		public const string ProjectCode = "ProjectCode";
		public const string ProjectPhaseCode = "ProjectPhaseCode";
		public const string Tags = "Tags";
		public const string EntraClientId = "EntraClientId";
		public const string EntraTenantId = "EntraTenantId";
		public const string EntraScopes = "EntraScopes";
	}

	private const string NotConnectedMessage = "Not connected to MCP server. Please use Test Connection in Settings to sign in first.";

	private readonly ITokenProviderFactory _tokenProviderFactory = tokenProviderFactory;

	private ITokenProvider? _tokenProvider;
	private string[]? _scopes;
	private string? _baseUrl;
	private McpClient? _mcpClient;
	private TokenInjectingHandler? _handler;
	private HttpClient? _httpClient;
	private string? _projectCode;
	private string? _projectPhaseCode;
	private string? _tags;

	public override PluginMetadata Metadata => new()
	{
		Id = "gorang3.worklog",
		Name = "Goran G3 Timesheets",
		Version = new Version(2, 0, 0),
		Author = "WorkTracker Team",
		Description = "Upload and read worklogs via Goran G3 MCP server",
		Tags = ["goran", "gorang3", "timetracking", "worklog", "mcp", "entra"]
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return new List<PluginConfigurationField>
		{
			new()
			{
				Key = ConfigKeys.GoranBaseUrl,
				Label = "Goran URL",
				Description = "The base URL of the Goran instance (e.g., https://moonfish-g3.goran.cz)",
				Type = PluginConfigurationFieldType.Url,
				IsRequired = true,
				Placeholder = "https://moonfish-g3.goran.cz",
				ValidationPattern = @"^https://.*",
				ValidationMessage = "Please enter a valid HTTPS URL"
			},
			new()
			{
				Key = ConfigKeys.ProjectCode,
				Label = "Project Code",
				Description = "Global project code for all worklogs (e.g., 000.GOR)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "000.GOR"
			},
			new()
			{
				Key = ConfigKeys.ProjectPhaseCode,
				Label = "Project Phase Code",
				Description = "Optional project phase code (e.g., SP, DEV)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = false,
				Placeholder = "Leave empty if not required"
			},
			new()
			{
				Key = ConfigKeys.Tags,
				Label = "Default Tags",
				Description = "Optional comma-separated list of default tags (e.g., review,bugfix)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = false,
				Placeholder = "tag1,tag2"
			},
			new()
			{
				Key = ConfigKeys.EntraClientId,
				Label = "Entra Client ID",
				Description = "Application (client) ID of the MCP Client app registration in Microsoft Entra",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "00000000-0000-0000-0000-000000000000"
			},
			new()
			{
				Key = ConfigKeys.EntraTenantId,
				Label = "Entra Tenant ID",
				Description = "Directory (tenant) ID, or 'organizations' for multi-tenant",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "00000000-0000-0000-0000-000000000000"
			},
			new()
			{
				Key = ConfigKeys.EntraScopes,
				Label = "Entra Scopes",
				Description = "API scopes for MCP access (e.g., api://{goran-api-client-id}/Mcp.Access)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "api://{client-id}/Mcp.Access"
			}
		};
	}

	protected override async Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		_baseUrl = GetRequiredConfigValue(ConfigKeys.GoranBaseUrl).TrimEnd('/');
		_projectCode = GetRequiredConfigValue(ConfigKeys.ProjectCode);
		_projectPhaseCode = GetConfigValue(ConfigKeys.ProjectPhaseCode);
		_tags = GetConfigValue(ConfigKeys.Tags);

		var clientId = GetRequiredConfigValue(ConfigKeys.EntraClientId);
		var tenantId = GetRequiredConfigValue(ConfigKeys.EntraTenantId);
		var scopesRaw = GetRequiredConfigValue(ConfigKeys.EntraScopes);
		_scopes = scopesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if (_scopes.Length == 0)
		{
			Logger.LogError("EntraScopes configuration is empty or contains only whitespace/commas");
			return false;
		}

		_tokenProvider = _tokenProviderFactory.Create(tenantId, clientId, _scopes);

		// Try silent auth only — interactive auth happens in TestConnectionAsync where progress is available
		var token = await _tokenProvider.AcquireTokenSilentAsync(cancellationToken);
		if (token != null)
		{
			try
			{
				await ConnectMcpAsync(cancellationToken);
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex, "MCP server unavailable during startup, will retry on next operation");
			}
		}

		return true;
	}

	public override async ValueTask DisposeAsync()
	{
		if (_mcpClient != null)
		{
			await _mcpClient.DisposeAsync();
			_mcpClient = null;
		}

		_httpClient?.Dispose();
		_httpClient = null;

		_handler?.Dispose();
		_handler = null;

		GC.SuppressFinalize(this);
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		try
		{
			if (_mcpClient == null)
			{
				progress?.Report("Authenticating...");

				var token = await _tokenProvider!.AcquireTokenSilentAsync(cancellationToken);
				if (token == null)
				{
					token = await _tokenProvider.AcquireTokenInteractiveAsync(progress, cancellationToken);
				}

				if (token == null)
				{
					return PluginResult<bool>.Failure("Authentication failed — could not acquire token.", PluginErrorCategory.Authentication);
				}

				progress?.Report("Connecting to MCP server...");
				await ConnectMcpAsync(cancellationToken);
			}

			var tools = await _mcpClient!.ListToolsAsync(cancellationToken: cancellationToken);
			var hasTimesheetTool = tools.Any(t => t.Name == "create_my_timesheet_item");

			if (!hasTimesheetTool)
			{
				return PluginResult<bool>.Failure("MCP server does not expose expected timesheet tools");
			}

			Logger.LogInformation("Successfully connected to Goran G3 MCP server ({ToolCount} tools available)", tools.Count);
			return PluginResult<bool>.Success(true);
		}
		catch (Exception ex)
		{
			// Reset connection state so next attempt will reconnect
			if (_mcpClient != null)
			{
				await _mcpClient.DisposeAsync();
				_mcpClient = null;
			}

			Logger.LogError(ex, "Connection test failed");
			return PluginResult<bool>.Failure($"Connection failed: {ex.Message}", PluginErrorCategory.Network);
		}
	}

	private async Task ConnectMcpAsync(CancellationToken cancellationToken)
	{
		if (_mcpClient != null)
		{
			await _mcpClient.DisposeAsync();
			_mcpClient = null;
		}

		_httpClient?.Dispose();
		_handler?.Dispose();

		_handler = new TokenInjectingHandler(_tokenProvider!);
		_httpClient = new HttpClient(_handler, disposeHandler: false);

		var transport = new HttpClientTransport(
			new HttpClientTransportOptions
			{
				Endpoint = new Uri($"{_baseUrl}/mcp"),
				Name = "GoranG3"
			},
			_httpClient,
			ownsHttpClient: false);

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

		try
		{
			_mcpClient = await McpClient.CreateAsync(transport, cancellationToken: timeoutCts.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"MCP server at {_baseUrl}/mcp did not respond within 30 seconds");
		}
	}

	public override async Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		if (_mcpClient == null)
		{
			return PluginResult<bool>.Failure(NotConnectedMessage, PluginErrorCategory.Authentication);
		}

		try
		{
			var arguments = new Dictionary<string, object?>
			{
				["project_code"] = _projectCode,
				["date"] = worklog.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				["duration_minutes"] = worklog.DurationMinutes,
				["start_time"] = worklog.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)
			};

			var text = BuildText(worklog);
			if (!string.IsNullOrWhiteSpace(text))
			{
				arguments["text"] = text;
			}

			if (!string.IsNullOrWhiteSpace(_projectPhaseCode))
			{
				arguments["project_phase_code"] = _projectPhaseCode;
			}

			var tags = ParseTags(_tags);
			if (tags != null)
			{
				arguments["tags"] = tags;
			}

			var externalId = ParseExternalId(worklog.TicketId);
			if (externalId.HasValue)
			{
				arguments["external_id"] = externalId.Value;
			}

			var result = await _mcpClient!.CallToolAsync("create_my_timesheet_item", arguments, cancellationToken: cancellationToken);

			if (result.IsError == true)
			{
				var errorText = GetResultText(result);
				Logger.LogWarning("Failed to upload worklog: {Error}", errorText);
				return PluginResult<bool>.Failure($"Upload failed: {errorText}", PluginErrorCategory.Network);
			}

			Logger.LogInformation(
				"Successfully uploaded worklog: {ProjectCode}, {Duration} minutes on {Date}",
				_projectCode, worklog.DurationMinutes, worklog.StartTime.Date.ToString("yyyy-MM-dd"));
			return PluginResult<bool>.Success(true);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Error uploading worklog to Goran via MCP");
			var errorCategory = ex switch
			{
				InvalidOperationException => PluginErrorCategory.Authentication,
				HttpRequestException => PluginErrorCategory.Network,
				IOException => PluginErrorCategory.Network,
				_ => PluginErrorCategory.Internal
			};
			return PluginResult<bool>.Failure($"Error: {ex.Message}", errorCategory);
		}
	}

	public override async Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		if (_mcpClient == null)
		{
			return PluginResult<WorklogSubmissionResult>.Failure(NotConnectedMessage, PluginErrorCategory.Authentication);
		}

		var worklogsByDate = worklogs.GroupBy(w => w.StartTime.Date).OrderBy(g => g.Key);
		var totalSuccessful = 0;
		var totalFailed = 0;
		var allErrors = new List<WorklogSubmissionError>();

		foreach (var dayGroup in worklogsByDate)
		{
			var dayResult = await base.UploadWorklogsAsync(dayGroup, cancellationToken);
			if (dayResult.IsFailure)
			{
				return dayResult;
			}

			totalSuccessful += dayResult.Value!.SuccessfulEntries;
			totalFailed += dayResult.Value!.FailedEntries;
			allErrors.AddRange(dayResult.Value!.Errors);

			var submitResult = await SubmitTimesheetAsync(dayGroup.Key, cancellationToken);
			if (submitResult.IsFailure)
			{
				allErrors.Add(new WorklogSubmissionError
				{
					Worklog = dayGroup.First(),
					ErrorMessage = $"Timesheet submit failed for {dayGroup.Key:yyyy-MM-dd}: {submitResult.Error}"
				});
			}
		}

		return PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
		{
			TotalEntries = totalSuccessful + totalFailed,
			SuccessfulEntries = totalSuccessful,
			FailedEntries = totalFailed,
			Errors = allErrors
		});
	}

	private async Task<PluginResult<bool>> SubmitTimesheetAsync(DateTime date, CancellationToken cancellationToken)
	{
		var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		try
		{
			var arguments = new Dictionary<string, object?>
			{
				["date"] = dateStr
			};

			var result = await _mcpClient!.CallToolAsync("submit_my_timesheet", arguments, cancellationToken: cancellationToken);

			if (result.IsError == true)
			{
				var errorText = GetResultText(result);
				Logger.LogWarning("Failed to submit timesheet for {Date}: {Error}", dateStr, errorText);
				return PluginResult<bool>.Failure($"Submit failed: {errorText}", PluginErrorCategory.Network);
			}

			Logger.LogInformation("Submitted timesheet for {Date}", dateStr);
			return PluginResult<bool>.Success(true);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Error submitting timesheet for {Date}", dateStr);
			return PluginResult<bool>.Failure($"Error: {ex.Message}");
		}
	}

	public override async Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		if (_mcpClient == null)
		{
			return PluginResult<IEnumerable<PluginWorklogEntry>>.Failure(NotConnectedMessage, PluginErrorCategory.Authentication);
		}

		try
		{
			var arguments = new Dictionary<string, object?>
			{
				["date_from"] = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				["date_to"] = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			};

			var result = await _mcpClient!.CallToolAsync("get_my_timesheet_items_list", arguments, cancellationToken: cancellationToken);

			if (result.IsError == true)
			{
				var errorText = GetResultText(result);
				return PluginResult<IEnumerable<PluginWorklogEntry>>.Failure($"Failed to get worklogs: {errorText}", PluginErrorCategory.Network);
			}

			var responseText = GetResultText(result);
			var worklogs = ParseTimesheetResponse(responseText);

			return PluginResult<IEnumerable<PluginWorklogEntry>>.Success(worklogs);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Error getting worklogs from Goran via MCP");
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
			w.StartTime.Date == worklog.StartTime.Date &&
			Math.Abs((w.StartTime.TimeOfDay - worklog.StartTime.TimeOfDay).TotalMinutes) < 1 &&
			w.DurationMinutes == worklog.DurationMinutes);

		return PluginResult<bool>.Success(exists);
	}

	private static string BuildText(PluginWorklogEntry worklog)
		=> BuildText(worklog.TicketId, worklog.Description);

	internal static string BuildText(string? ticketId, string? description)
	{
		var parts = new List<string>();
		if (!string.IsNullOrWhiteSpace(ticketId))
		{
			parts.Add(ticketId);
		}

		if (!string.IsNullOrWhiteSpace(description))
		{
			parts.Add(description);
		}

		return string.Join(" - ", parts);
	}

	internal static string[]? ParseTags(string? tagsConfig)
	{
		if (string.IsNullOrWhiteSpace(tagsConfig))
		{
			return null;
		}

		var tags = tagsConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return tags.Length > 0 ? tags : null;
	}

	internal static int? ParseExternalId(string? ticketId)
	{
		if (string.IsNullOrWhiteSpace(ticketId))
		{
			return null;
		}

		// Extract numeric ID from ticket ID like "PROJ-123" → 123, or return null if not numeric
		var lastDash = ticketId.LastIndexOf('-');
		var numberPart = lastDash >= 0 ? ticketId[(lastDash + 1)..] : ticketId;

		return int.TryParse(numberPart, out var id) ? id : null;
	}

	private static string GetResultText(CallToolResult result)
	{
		return string.Join(Environment.NewLine,
			result.Content
				.Where(c => c is TextContentBlock)
				.Cast<TextContentBlock>()
				.Select(c => c.Text));
	}

	private List<PluginWorklogEntry> ParseTimesheetResponse(string responseText)
	{
		var (worklogs, failedLines) = ParseTimesheetResponseCore(responseText);

		foreach (var failedLine in failedLines)
		{
			Logger.LogWarning("Failed to parse timesheet line: {Line}", failedLine);
		}

		return worklogs;
	}

	internal static (List<PluginWorklogEntry> Worklogs, List<string> FailedLines) ParseTimesheetResponseCore(string responseText)
	{
		var worklogs = new List<PluginWorklogEntry>();
		var failedLines = new List<string>();

		if (string.IsNullOrWhiteSpace(responseText))
		{
			return (worklogs, failedLines);
		}

		// The MCP server returns a text table. Parse each line that contains timesheet data.
		// Expected columns: Date | Project | Phase | Description | Start | Duration | Status | Tags
		var lines = responseText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		// Skip header lines (typically first 2 lines: header + separator)
		var dataStartIndex = 0;
		for (var i = 0; i < lines.Length; i++)
		{
			if (lines[i].Contains("---") || lines[i].Contains("==="))
			{
				dataStartIndex = i + 1;
			}
		}

		for (var i = dataStartIndex; i < lines.Length; i++)
		{
			var line = lines[i].Trim();

			// Skip empty lines, separator lines, and the total line
			if (string.IsNullOrWhiteSpace(line) ||
				line.Contains("---") ||
				line.Contains("===") ||
				line.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			try
			{
				var entry = ParseTimesheetLine(line);
				if (entry != null)
				{
					worklogs.Add(entry);
				}
			}
			catch
			{
				failedLines.Add(line);
			}
		}

		return (worklogs, failedLines);
	}

	internal static PluginWorklogEntry? ParseTimesheetLine(string line)
	{
		// Split by pipe delimiter (common in MCP text table responses)
		var columns = line.Split('|', StringSplitOptions.TrimEntries);

		// Need at least Date, Project, Description, Start, Duration
		if (columns.Length < 5)
		{
			return null;
		}

		// Try to find date column (first column that looks like a date)
		DateTime? date = null;
		string? project = null;
		string? description = null;
		TimeSpan? startTime = null;
		int? durationMinutes = null;

		foreach (var col in columns)
		{
			if (date == null && DateTime.TryParseExact(col, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
			{
				date = d;
			}
			else if (startTime == null && TimeSpan.TryParseExact(col, "h\\:mm", CultureInfo.InvariantCulture, out var t))
			{
				startTime = t;
			}
			else if (durationMinutes == null && TryParseDuration(col, out var dur))
			{
				durationMinutes = dur;
			}
		}

		if (date == null || startTime == null || durationMinutes == null)
		{
			return null;
		}

		// Heuristic: project is typically the second column, description after that
		var nonEmptyColumns = columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
		if (nonEmptyColumns.Length >= 3)
		{
			project = nonEmptyColumns.Length > 1 ? nonEmptyColumns[1] : null;
			description = nonEmptyColumns.Length > 3 ? nonEmptyColumns[3] : nonEmptyColumns.Length > 2 ? nonEmptyColumns[2] : null;
		}

		var entryStart = date.Value.Add(startTime.Value);

		return new PluginWorklogEntry
		{
			ProjectName = project,
			Description = description,
			StartTime = entryStart,
			EndTime = entryStart.AddMinutes(durationMinutes.Value),
			DurationMinutes = durationMinutes.Value
		};
	}

	internal static bool TryParseDuration(string text, out int minutes)
	{
		minutes = 0;

		// Try direct integer (minutes)
		if (int.TryParse(text, out minutes))
		{
			return minutes >= 0;
		}

		// Try "h:mm" format (e.g., "2:30")
		if (text.Contains(':') &&
			TimeSpan.TryParseExact(text, "h\\:mm", CultureInfo.InvariantCulture, out var ts))
		{
			minutes = (int)ts.TotalMinutes;
			return true;
		}

		return false;
	}

}
