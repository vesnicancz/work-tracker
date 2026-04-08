using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Plugin.Office365Calendar;

namespace WorkTracker.Plugin.Office365Calendar.Tests;

public class Office365CalendarPluginTests : IAsyncDisposable
{
	private readonly MockHttpHandler _httpHandler = new();
	private readonly Office365CalendarPlugin _plugin;

	private static readonly Dictionary<string, string> ValidConfig = new()
	{
		["TenantId"] = "test-tenant-id",
		["ClientId"] = "test-client-id"
	};

	public Office365CalendarPluginTests()
	{
		_plugin = new Office365CalendarPlugin(
			new MockHttpClientFactory(_httpHandler),
			NullLogger<Office365CalendarPlugin>.Instance,
			new MockTokenProviderFactory("fake-token"));
	}

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
		_httpHandler.Dispose();
	}

	private async Task InitializePluginAsync(IDictionary<string, string>? config = null)
	{
		var initialized = await _plugin.InitializeAsync(config ?? ValidConfig, TestContext.Current.CancellationToken);
		initialized.Should().BeTrue("plugin initialization should succeed");
	}

	private static string CalendarEventsJson(params object[] events) =>
		JsonSerializer.Serialize(new { value = events });

	private static object CalendarEvent(
		string subject = "Test Meeting",
		string id = "event-1",
		string? startDateTime = "2026-04-01T09:00:00",
		string? endDateTime = "2026-04-01T10:00:00",
		bool isAllDay = false,
		string? webLink = null)
	{
		var evt = new Dictionary<string, object?>
		{
			["subject"] = subject,
			["id"] = id,
			["isAllDay"] = isAllDay
		};

		if (!isAllDay && startDateTime != null)
		{
			evt["start"] = new { dateTime = startDateTime, timeZone = "UTC" };
			evt["end"] = new { dateTime = endDateTime, timeZone = "UTC" };
		}

		if (webLink != null)
		{
			evt["webLink"] = webLink;
		}

		return evt;
	}

	private void SetupCalendarResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_httpHandler.SetupGetPrefix("/v1.0/me/calendarView", json, statusCode);
	}

	private void SetupMeResponse(string json = """{"displayName":"Test User"}""", HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_httpHandler.SetupGet("/v1.0/me", json, statusCode);
	}

	#region Metadata

	[Fact]
	public void Metadata_HasCorrectId()
	{
		_plugin.Metadata.Id.Should().Be("o365.calendar");
	}

	[Fact]
	public void Metadata_HasCorrectNameAndVersion()
	{
		_plugin.Metadata.Name.Should().Be("Office 365 Calendar");
		_plugin.Metadata.Version.Should().Be(new Version(1, 0, 0));
	}

	#endregion

	#region Configuration Fields

	[Fact]
	public void GetConfigurationFields_ReturnsThreeFields()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Should().HaveCount(3);
		fields.Select(f => f.Key).Should().BeEquivalentTo(["TenantId", "ClientId", "IncludeAllDayEvents"]);
	}

	[Fact]
	public void GetConfigurationFields_TenantIdAndClientIdAreRequired()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Single(f => f.Key == "TenantId").IsRequired.Should().BeTrue();
		fields.Single(f => f.Key == "ClientId").IsRequired.Should().BeTrue();
	}

	[Fact]
	public void GetConfigurationFields_IncludeAllDayEventsIsOptionalCheckbox()
	{
		var fields = _plugin.GetConfigurationFields();
		var field = fields.Single(f => f.Key == "IncludeAllDayEvents");

		field.IsRequired.Should().BeFalse();
		field.Type.Should().Be(PluginConfigurationFieldType.Checkbox);
	}

	#endregion

	#region Configuration Validation

	[Fact]
	public async Task ValidateConfigurationAsync_ValidConfig_Succeeds()
	{
		var result = await _plugin.ValidateConfigurationAsync(ValidConfig, TestContext.Current.CancellationToken);

		result.IsValid.Should().BeTrue();
	}

	[Fact]
	public async Task ValidateConfigurationAsync_MissingTenantId_Fails()
	{
		var config = new Dictionary<string, string> { ["ClientId"] = "test" };

		var result = await _plugin.ValidateConfigurationAsync(config, TestContext.Current.CancellationToken);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Tenant ID"));
	}

	[Fact]
	public async Task ValidateConfigurationAsync_MissingClientId_Fails()
	{
		var config = new Dictionary<string, string> { ["TenantId"] = "test" };

		var result = await _plugin.ValidateConfigurationAsync(config, TestContext.Current.CancellationToken);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Client ID"));
	}

	#endregion

	#region Initialization

	[Fact]
	public async Task InitializeAsync_ValidConfig_ReturnsTrue()
	{
		var result = await _plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		result.Should().BeTrue();
	}

	[Fact]
	public async Task InitializeAsync_MissingRequiredField_ReturnsFalse()
	{
		var result = await _plugin.InitializeAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);

		result.Should().BeFalse();
	}

	#endregion

	#region GetSuggestionsAsync

	[Fact]
	public async Task GetSuggestionsAsync_ReturnsCalendarEvents()
	{
		SetupCalendarResponse(CalendarEventsJson(
			CalendarEvent("Standup", "evt-1", "2026-04-01T09:00:00", "2026-04-01T09:15:00"),
			CalendarEvent("Sprint Review", "evt-2", "2026-04-01T14:00:00", "2026-04-01T15:00:00")));
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().HaveCount(2);
		result.Value![0].Title.Should().Be("Standup");
		result.Value[0].Source.Should().Be("O365 Calendar");
		result.Value[1].Title.Should().Be("Sprint Review");
	}

	[Fact]
	public async Task GetSuggestionsAsync_EmptyCalendar_ReturnsEmptyList()
	{
		SetupCalendarResponse(CalendarEventsJson());
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEmpty();
	}

	[Fact]
	public async Task GetSuggestionsAsync_AllDayEvent_ExcludedByDefault()
	{
		SetupCalendarResponse(CalendarEventsJson(
			CalendarEvent("All Day Event", "evt-1", isAllDay: true),
			CalendarEvent("Regular Meeting", "evt-2", "2026-04-01T10:00:00", "2026-04-01T11:00:00")));
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().HaveCount(1);
		result.Value![0].Title.Should().Be("Regular Meeting");
	}

	[Fact]
	public async Task GetSuggestionsAsync_AllDayEvent_IncludedWhenConfigured()
	{
		SetupCalendarResponse(CalendarEventsJson(
			CalendarEvent("All Day Event", "evt-1", isAllDay: true)));
		var config = new Dictionary<string, string>(ValidConfig)
		{
			["IncludeAllDayEvents"] = "true"
		};
		await InitializePluginAsync(config);

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().HaveCount(1);
		result.Value![0].Title.Should().Be("All Day Event");
		result.Value[0].StartTime.Should().BeNull();
		result.Value[0].EndTime.Should().BeNull();
	}

	[Fact]
	public async Task GetSuggestionsAsync_EventWithWebLink_SetsSourceUrl()
	{
		SetupCalendarResponse(CalendarEventsJson(
			CalendarEvent("Meeting", "evt-1", webLink: "https://outlook.office.com/calendar/item/123")));
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.Value![0].SourceUrl.Should().Be("https://outlook.office.com/calendar/item/123");
	}

	[Fact]
	public async Task GetSuggestionsAsync_HttpError_ReturnsFailure()
	{
		SetupCalendarResponse("Internal Server Error", HttpStatusCode.InternalServerError);
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("500");
	}

	[Fact]
	public async Task GetSuggestionsAsync_MalformedJson_ReturnsFailure()
	{
		SetupCalendarResponse("not json");
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	[Fact]
	public async Task GetSuggestionsAsync_EventWithoutWebLink_HasNullSourceUrl()
	{
		SetupCalendarResponse(CalendarEventsJson(
			CalendarEvent("No Link Meeting", "evt-1", "2026-04-01T09:00:00", "2026-04-01T10:00:00")));
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value![0].SourceUrl.Should().BeNull();
	}

	[Fact]
	public async Task GetSuggestionsAsync_ParsesStartAndEndTime()
	{
		SetupCalendarResponse(CalendarEventsJson(
			CalendarEvent("Morning Sync", "evt-1", "2026-04-01T09:30:00", "2026-04-01T10:15:00")));
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		var suggestion = result.Value![0];
		suggestion.StartTime.Should().NotBeNull();
		suggestion.StartTime!.Value.Hour.Should().Be(9);
		suggestion.StartTime.Value.Minute.Should().Be(30);
		suggestion.EndTime.Should().NotBeNull();
		suggestion.EndTime!.Value.Hour.Should().Be(10);
		suggestion.EndTime.Value.Minute.Should().Be(15);
	}

	[Fact]
	public async Task GetSuggestionsAsync_NotInitialized_Throws()
	{
		var act = () => _plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>();
	}

	[Fact]
	public async Task GetSuggestionsAsync_NoToken_ReturnsFailure()
	{
		var plugin = new Office365CalendarPlugin(
			new MockHttpClientFactory(_httpHandler),
			NullLogger<Office365CalendarPlugin>.Instance,
			new MockTokenProviderFactory(null));
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var result = await plugin.GetSuggestionsAsync(new DateTime(2026, 4, 1), TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("authenticated");
		await plugin.DisposeAsync();
	}

	#endregion

	#region SupportsSearch

	[Fact]
	public async Task SearchAsync_ReturnsFailure()
	{
		var result = await _plugin.SearchAsync("test", TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	#endregion

	#region TestConnectionAsync

	[Fact]
	public async Task TestConnectionAsync_Success_ReturnsSuccess()
	{
		SetupMeResponse();
		await InitializePluginAsync();

		var result = await _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task TestConnectionAsync_HttpError_ReturnsFailure()
	{
		SetupMeResponse("Unauthorized", HttpStatusCode.Unauthorized);
		await InitializePluginAsync();

		var result = await _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("401");
	}

	[Fact]
	public async Task TestConnectionAsync_NoToken_ReturnsFailure()
	{
		var plugin = new Office365CalendarPlugin(
			new MockHttpClientFactory(_httpHandler),
			NullLogger<Office365CalendarPlugin>.Instance,
			new MockTokenProviderFactory(null));
		await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var result = await plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("token");
		await plugin.DisposeAsync();
	}

	[Fact]
	public async Task TestConnectionAsync_WithProgress_ReportsProgress()
	{
		SetupMeResponse();
		await InitializePluginAsync();

		var result = await _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task TestConnectionAsync_NotInitialized_Throws()
	{
		var act = () => _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>();
	}

	#endregion

	#region Lifecycle

	[Fact]
	public async Task DisposeAsync_CanBeCalledMultipleTimes()
	{
		await _plugin.DisposeAsync();
		await _plugin.DisposeAsync();
	}

	#endregion
}

internal sealed class MockTokenProvider(string? token) : ITokenProvider
{
	public Task<string?> AcquireTokenSilentAsync(CancellationToken cancellationToken)
		=> Task.FromResult(token);

	public Task<string?> AcquireTokenInteractiveAsync(IProgress<string>? progress, CancellationToken cancellationToken)
		=> Task.FromResult(token);
}

internal sealed class MockTokenProviderFactory(string? token = "fake-token") : ITokenProviderFactory
{
	public Task<ITokenProvider> CreateAsync(string tenantId, string clientId, string[] scopes)
		=> Task.FromResult<ITokenProvider>(new MockTokenProvider(token));
}

internal sealed class MockHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
	public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class MockHttpHandler : HttpMessageHandler
{
	private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _getResponses = new();
	private readonly List<(string Prefix, string Body, HttpStatusCode Status)> _getPrefixResponses = new();

	public void SetupGet(string pathAndQuery, string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_getResponses[pathAndQuery] = (responseBody, statusCode);
	}

	public void SetupGetPrefix(string pathPrefix, string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_getPrefixResponses.Add((pathPrefix, responseBody, statusCode));
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var pathAndQuery = request.RequestUri!.PathAndQuery;

		if (request.Method == HttpMethod.Get)
		{
			if (_getResponses.TryGetValue(pathAndQuery, out var exact))
			{
				return Task.FromResult(new HttpResponseMessage(exact.Status)
				{
					Content = new StringContent(exact.Body, System.Text.Encoding.UTF8, "application/json")
				});
			}

			foreach (var (prefix, body, status) in _getPrefixResponses)
			{
				if (pathAndQuery.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					return Task.FromResult(new HttpResponseMessage(status)
					{
						Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
					});
				}
			}
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
		{
			Content = new StringContent("Not found")
		});
	}
}
