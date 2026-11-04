using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3;

/// <summary>
/// Plugin for uploading worklogs via Goran G3 MCP server.
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

	private const string NotConnectedMessage = "Not connected to MCP server. Check that it is reachable, or use Test Connection in Settings to sign in.";

	private readonly ITokenProviderFactory _tokenProviderFactory = tokenProviderFactory;
	private readonly TimesheetDayBatch _dayBatch = new();

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
		var scopes = GetRequiredConfigValue(ConfigKeys.EntraScopes)
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		// Before anything changes, so that a rejected configuration leaves the plugin as it was
		if (scopes.Length == 0)
		{
			Logger.LogError("EntraScopes configuration is empty or contains only whitespace/commas");
			return false;
		}

		// The configuration may now point at another server, where neither this connection nor the
		// remembered entries belong
		await DisconnectAsync();
		_dayBatch.Reset();

		_scopes = scopes;
		_baseUrl = GetRequiredConfigValue(ConfigKeys.GoranBaseUrl).TrimEnd('/');
		_projectCode = GetRequiredConfigValue(ConfigKeys.ProjectCode);
		_projectPhaseCode = GetConfigValue(ConfigKeys.ProjectPhaseCode);
		_tags = GetConfigValue(ConfigKeys.Tags);

		var clientId = GetRequiredConfigValue(ConfigKeys.EntraClientId);
		var tenantId = GetRequiredConfigValue(ConfigKeys.EntraTenantId);

		_tokenProvider = await _tokenProviderFactory.CreateAsync(tenantId, clientId, _scopes, InteractiveAuthFlow.DeviceCode);

		await EnsureConnectedAsync(cancellationToken);

		return true;
	}

	protected override async ValueTask OnDisposeAsync()
	{
		await DisconnectAsync();
	}

	/// <summary>
	/// Connects if there is no connection, using silent authentication only: signing in interactively
	/// needs the progress reporting of <see cref="TestConnectionAsync"/>. There can be no connection
	/// because the server was down or the sign-in had expired, and neither should take a restart to fix.
	/// </summary>
	private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
	{
		if (_mcpClient != null)
		{
			return true;
		}

		try
		{
			if (await _tokenProvider!.AcquireTokenSilentAsync(cancellationToken) == null)
			{
				return false;
			}

			await ConnectMcpAsync(cancellationToken);
			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "MCP server is not available, will try again on the next operation");
			return false;
		}
	}

	private async Task DisconnectAsync()
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
			await DisconnectAsync();

			Logger.LogError(ex, "Connection test failed");
			return PluginResult<bool>.Failure($"Connection failed: {ex.Message}", PluginErrorCategory.Network);
		}
	}

	private async Task ConnectMcpAsync(CancellationToken cancellationToken)
	{
		await DisconnectAsync();

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

		if (!await EnsureConnectedAsync(cancellationToken))
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
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// The connection may be what broke; the next operation starts with a new one
			await DisconnectAsync();

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

	public override async Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		if (!SupportedModes.HasFlag(mode))
		{
			return PluginResult<WorklogSubmissionResult>.Failure(
				$"Goran G3 does not support submission mode '{mode}'",
				PluginErrorCategory.Validation);
		}

		if (!await EnsureConnectedAsync(cancellationToken))
		{
			return PluginResult<WorklogSubmissionResult>.Failure(NotConnectedMessage, PluginErrorCategory.Authentication);
		}

		return await _dayBatch.RunAsync(
			worklogs,
			day => base.UploadWorklogsAsync(day, mode, cancellationToken),
			date => SubmitTimesheetAsync(date, cancellationToken));
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
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// The connection may be what broke; the next operation starts with a new one
			await DisconnectAsync();

			Logger.LogError(ex, "Error submitting timesheet for {Date}", dateStr);
			return PluginResult<bool>.Failure($"Error: {ex.Message}");
		}
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
}
