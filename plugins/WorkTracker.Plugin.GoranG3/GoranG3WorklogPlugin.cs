using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3;

/// <summary>
/// Plugin for uploading worklogs to Goran G3 Timesheets via HTTP GET endpoint.
/// </summary>
public sealed class GoranG3WorklogPlugin : WorklogUploadPluginBase, IDisposable
{
	private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
	private const int MaxRetries = 2;

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

	private HttpClient? _httpClient;
	private IPublicClientApplication? _msalApp;
	private string[]? _scopes;
	private string? _baseUrl;
	private string? _projectCode;
	private string? _projectPhaseCode;
	private string? _tags;
	private bool _disposed;

	public override PluginMetadata Metadata => new()
	{
		Id = "gorang3.worklog",
		Name = "Goran G3 Timesheets",
		Version = new Version(1, 0, 0),
		Author = "WorkTracker Team",
		Description = "Upload worklogs to Goran G3 Timesheets (moonfish/Goran time tracking system)",
		Tags = ["goran", "gorang3", "timetracking", "worklog", "entra"]
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
				Description = "Application (client) ID from Microsoft Entra app registration",
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
				Description = "API scopes for token acquisition (e.g., api://client-id/user_impersonation)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "api://client-id/user_impersonation"
			}
		};
	}

	protected override Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		try
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
				Logger?.LogError("EntraScopes configuration is empty or contains only whitespace/commas");
				return Task.FromResult(false);
			}

			_msalApp = PublicClientApplicationBuilder
				.Create(clientId)
				.WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
				.WithDefaultRedirectUri()
				.Build();

			_httpClient = new HttpClient
			{
				Timeout = HttpTimeout
			};

			Logger?.LogInformation("Goran G3 plugin initialized for {BaseUrl}, project {ProjectCode}", _baseUrl, _projectCode);

			return Task.FromResult(true);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Failed to initialize Goran G3 plugin");
			return Task.FromResult(false);
		}
	}

	protected override Task OnShutdownAsync()
	{
		Dispose();
		return Task.CompletedTask;
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken)
	{
		if (_httpClient == null || _msalApp == null)
		{
			return PluginResult<bool>.Failure("Plugin is not properly initialized");
		}

		try
		{
			var token = await AcquireTokenAsync(cancellationToken);
			if (string.IsNullOrEmpty(token))
			{
				return PluginResult<bool>.Failure("Failed to acquire authentication token");
			}

			Logger?.LogInformation("Successfully authenticated with Microsoft Entra ID");
			return PluginResult<bool>.Success(true);
		}
		catch (MsalException ex)
		{
			Logger?.LogError(ex, "MSAL authentication failed");
			return PluginResult<bool>.Failure($"Authentication failed: {ex.Message}");
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

		try
		{
			var token = await AcquireTokenAsync(cancellationToken);
			if (string.IsNullOrEmpty(token))
			{
				return PluginResult<bool>.Failure("Failed to acquire authentication token");
			}

			var url = BuildCreateUrl(worklog);

			Logger?.LogDebug("Uploading worklog to Goran: {Url}", url);

			string? lastError = null;
			for (var attempt = 0; attempt <= MaxRetries; attempt++)
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, url);
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

				using var response = await _httpClient!.SendAsync(request, cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
					var truncatedResponse = responseText.Length > 500 ? responseText[..500] + "..." : responseText;
					Logger?.LogInformation(
						"Successfully uploaded worklog: {ProjectCode}, {Duration} minutes on {Date}. Response: {Response}",
						_projectCode, worklog.DurationMinutes, worklog.StartTime.Date.ToString("yyyy-MM-dd"), truncatedResponse);
					return PluginResult<bool>.Success(true);
				}

				var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var statusCode = (int)response.StatusCode;
				lastError = $"Upload failed: {response.StatusCode} - {errorContent}";

				if (attempt < MaxRetries && (statusCode >= 500 || statusCode == 429))
				{
					var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
					Logger?.LogWarning("Goran API returned {StatusCode}, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
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
			Logger?.LogError(ex, "Error uploading worklog to Goran");
			return PluginResult<bool>.Failure($"Error: {ex.Message}");
		}
	}

	public override Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		return Task.FromResult(
			PluginResult<IEnumerable<PluginWorklogEntry>>.Failure("Not supported by Goran G3 plugin — no read API available"));
	}

	public override Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		return Task.FromResult(
			PluginResult<bool>.Failure("Not supported by Goran G3 plugin — no read API available"));
	}

	private string BuildCreateUrl(PluginWorklogEntry worklog)
	{
		var textParts = new List<string>();
		if (!string.IsNullOrWhiteSpace(worklog.TicketId))
		{
			textParts.Add(worklog.TicketId);
		}

		if (!string.IsNullOrWhiteSpace(worklog.Description))
		{
			textParts.Add(worklog.Description);
		}

		var text = string.Join(" - ", textParts);

		var queryParams = new List<string>
		{
			$"projectCode={Uri.EscapeDataString(_projectCode!)}",
			$"date={worklog.StartTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
			$"durationMinutes={worklog.DurationMinutes}",
			$"startTime={worklog.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture)}",
			$"responseMode=plain"
		};

		if (!string.IsNullOrWhiteSpace(text))
		{
			queryParams.Add($"text={Uri.EscapeDataString(text)}");
		}

		if (!string.IsNullOrWhiteSpace(_projectPhaseCode))
		{
			queryParams.Add($"projectPhaseCode={Uri.EscapeDataString(_projectPhaseCode)}");
		}

		if (!string.IsNullOrWhiteSpace(_tags))
		{
			queryParams.Add($"tags={Uri.EscapeDataString(_tags)}");
		}

		return $"{_baseUrl}/timesheets/my/create?{string.Join("&", queryParams)}";
	}

	private async Task<string?> AcquireTokenAsync(CancellationToken cancellationToken)
	{
		var accounts = await _msalApp!.GetAccountsAsync().ConfigureAwait(false);
		var firstAccount = accounts.FirstOrDefault();

		try
		{
			if (firstAccount != null)
			{
				var silentResult = await _msalApp
					.AcquireTokenSilent(_scopes!, firstAccount)
					.ExecuteAsync(cancellationToken)
					.ConfigureAwait(false);
				return silentResult.AccessToken;
			}
		}
		catch (MsalUiRequiredException)
		{
			Logger?.LogDebug("Silent token acquisition failed, falling back to interactive login");
		}

		var interactiveResult = await _msalApp
			.AcquireTokenInteractive(_scopes!)
			.ExecuteAsync(cancellationToken)
			.ConfigureAwait(false);
		return interactiveResult.AccessToken;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_httpClient?.Dispose();
			_httpClient = null;
			_disposed = true;
		}
	}
}