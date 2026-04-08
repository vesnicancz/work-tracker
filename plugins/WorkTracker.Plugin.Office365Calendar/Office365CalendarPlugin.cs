using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Office365Calendar;

/// <summary>
/// Plugin that suggests work items based on Office 365 calendar events
/// </summary>
public sealed class Office365CalendarPlugin(
	IHttpClientFactory httpClientFactory,
	ILogger<Office365CalendarPlugin> logger,
	ITokenProviderFactory tokenProviderFactory)
	: WorkSuggestionPluginBase(logger)
{
	private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
	private static readonly string[] GraphScopes = ["Calendars.Read", "User.Read"];

	private static class ConfigKeys
	{
		public const string TenantId = "TenantId";
		public const string ClientId = "ClientId";
		public const string IncludeAllDayEvents = "IncludeAllDayEvents";
	}

	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
	private readonly ITokenProviderFactory _tokenProviderFactory = tokenProviderFactory;

	private ITokenProvider? _tokenProvider;
	private HttpClient? _httpClient;
	private bool _includeAllDayEvents;

	public override PluginMetadata Metadata => new()
	{
		Id = "o365.calendar",
		Name = "Office 365 Calendar",
		Version = new Version(1, 0, 0),
		Author = "WorkTracker Team",
		Description = "Suggests work items based on Office 365 calendar events via Microsoft Graph API",
		Website = "https://learn.microsoft.com/en-us/graph/api/calendar-list-calendarview",
		IconName = "CalendarMonth",
		Tags = ["office365", "calendar", "suggestions", "microsoft", "graph"]
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return
		[
			new()
			{
				Key = ConfigKeys.TenantId,
				Label = "Tenant ID",
				Description = "Azure AD Tenant ID (or 'organizations' for multi-tenant)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "00000000-0000-0000-0000-000000000000"
			},
			new()
			{
				Key = ConfigKeys.ClientId,
				Label = "Client ID",
				Description = "Azure AD App Registration Client ID (requires Calendars.Read and User.Read permissions)",
				Type = PluginConfigurationFieldType.Text,
				IsRequired = true,
				Placeholder = "00000000-0000-0000-0000-000000000000"
			},
			new()
			{
				Key = ConfigKeys.IncludeAllDayEvents,
				Label = "Include All-Day Events",
				Description = "Whether to include all-day events as suggestions",
				Type = PluginConfigurationFieldType.Checkbox,
				IsRequired = false,
				DefaultValue = "false"
			}
		];
	}

	protected override async Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		var tenantId = GetRequiredConfigValue(ConfigKeys.TenantId);
		var clientId = GetRequiredConfigValue(ConfigKeys.ClientId);
		_includeAllDayEvents = string.Equals(GetConfigValue(ConfigKeys.IncludeAllDayEvents), "true", StringComparison.OrdinalIgnoreCase);

		_tokenProvider = await _tokenProviderFactory.CreateAsync(tenantId, clientId, GraphScopes);

		_httpClient?.Dispose();
		_httpClient = _httpClientFactory.CreateClient();
		_httpClient.Timeout = HttpTimeout;

		return true;
	}

	protected override ValueTask OnDisposeAsync()
	{
		_httpClient?.Dispose();

		return ValueTask.CompletedTask;
	}

	public override async Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		try
		{
			var token = await _tokenProvider!.AcquireTokenInteractiveAsync(progress, cancellationToken);
			if (token == null)
			{
				return PluginResult<bool>.Failure("Authentication failed — could not acquire token.", PluginErrorCategory.Authentication);
			}

			using var response = await SendAuthenticatedAsync(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me", token, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				return PluginResult<bool>.Success(true);
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			return PluginResult<bool>.Failure($"Graph API returned {(int)response.StatusCode}: {body}", PluginErrorCategory.Network);
		}
		catch (Exception ex)
		{
			return PluginResult<bool>.Failure($"Connection failed: {ex.Message}");
		}
	}

	public override async Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(
		DateTime date, CancellationToken cancellationToken)
	{
		EnsureInitialized();

		try
		{
			var token = await _tokenProvider!.AcquireTokenSilentAsync(cancellationToken);
			if (token == null)
			{
				return PluginResult<IReadOnlyList<WorkSuggestion>>.Failure(
					"Not authenticated — please use Test Connection in Settings to sign in first.", PluginErrorCategory.Authentication);
			}

			var endLocal = date.Date.AddDays(1).AddSeconds(-1);
			var startDto = new DateTimeOffset(date.Date, TimeZoneInfo.Local.GetUtcOffset(date.Date));
			var endDto = new DateTimeOffset(endLocal, TimeZoneInfo.Local.GetUtcOffset(endLocal));
			var startDateTime = Uri.EscapeDataString(startDto.ToString("o"));
			var endDateTime = Uri.EscapeDataString(endDto.ToString("o"));

			var url = $"https://graph.microsoft.com/v1.0/me/calendarView" +
				$"?startDateTime={startDateTime}" +
				$"&endDateTime={endDateTime}" +
				$"&$select=subject,start,end,webLink,id,isAllDay" +
				$"&$orderby=start/dateTime";

			using var response = await SendAuthenticatedAsync(HttpMethod.Get, url, token, cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
				Logger.LogWarning("Graph API calendarView failed with {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
				return PluginResult<IReadOnlyList<WorkSuggestion>>.Failure(
					$"Calendar fetch failed ({(int)response.StatusCode}): {errorBody}", PluginErrorCategory.Network);
			}

			var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
			var suggestions = new List<WorkSuggestion>();

			if (json.TryGetProperty("value", out var events))
			{
				foreach (var evt in events.EnumerateArray())
				{
					var isAllDay = evt.TryGetProperty("isAllDay", out var allDayProp) && allDayProp.GetBoolean();
					if (isAllDay && !_includeAllDayEvents)
					{
						continue;
					}

					var subject = evt.GetProperty("subject").GetString() ?? "(No subject)";
					var eventId = evt.GetProperty("id").GetString() ?? "";
					var webLink = evt.TryGetProperty("webLink", out var linkProp) ? linkProp.GetString() : null;

					DateTime? startTime = null;
					DateTime? endTime = null;

					if (!isAllDay)
					{
						if (evt.TryGetProperty("start", out var startObj) &&
							startObj.TryGetProperty("dateTime", out var startDt))
						{
							startTime = DateTime.Parse(startDt.GetString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
						}

						if (evt.TryGetProperty("end", out var endObj) &&
							endObj.TryGetProperty("dateTime", out var endDt))
						{
							endTime = DateTime.Parse(endDt.GetString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
						}
					}

					suggestions.Add(new WorkSuggestion
					{
						Title = subject,
						StartTime = startTime,
						EndTime = endTime,
						Source = "O365 Calendar",
						SourceId = eventId,
						SourceUrl = webLink
					});
				}
			}

			Logger.LogInformation("Fetched {Count} calendar suggestions for {Date}", suggestions.Count, date.ToShortDateString());
			return PluginResult<IReadOnlyList<WorkSuggestion>>.Success(suggestions);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Failed to fetch calendar suggestions");
			return PluginResult<IReadOnlyList<WorkSuggestion>>.Failure($"Failed to fetch calendar: {ex.Message}");
		}
	}

	private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string url, string token, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, url);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		// Request times in user's local timezone so parsed DateTimes are correct
		request.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
		return await _httpClient!.SendAsync(request, cancellationToken);
	}

}
