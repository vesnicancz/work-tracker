using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Atlassian.Tests;

public class TempoWorklogPluginTests : IAsyncDisposable
{
    private readonly MockHttpHandler _httpHandler = new();
    private readonly TempoWorklogPlugin _plugin;

    private static readonly Dictionary<string, string> ValidConfig = new()
    {
        ["TempoBaseUrl"] = "https://api.eu.tempo.io/4",
        ["TempoApiToken"] = "tempo-token",
        [JiraConfigFields.JiraBaseUrl] = "https://test.atlassian.net",
        [JiraConfigFields.JiraEmail] = "user@example.com",
        [JiraConfigFields.JiraApiToken] = "jira-token",
        ["JiraAccountId"] = "account-123"
    };

    public TempoWorklogPluginTests()
    {
        _plugin = new TempoWorklogPlugin(
            new MockHttpClientFactory(_httpHandler),
            NullLogger<TempoWorklogPlugin>.Instance)
        {
            RetryDelayStrategy = _ => TimeSpan.Zero
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _plugin.DisposeAsync();
        _httpHandler.Dispose();
    }

    private async Task InitializePluginAsync()
    {
        var initialized = await _plugin.InitializeAsync(ValidConfig);
        initialized.Should().BeTrue("plugin initialization should succeed for the test configuration");
    }

    private static PluginWorklogEntry CreateWorklog(
        string ticketId = "PROJ-123",
        int durationMinutes = 60,
        DateTime? startTime = null) => new()
    {
        TicketId = ticketId,
        Description = "Test work",
        StartTime = startTime ?? new DateTime(2026, 4, 1, 9, 0, 0),
        EndTime = (startTime ?? new DateTime(2026, 4, 1, 9, 0, 0)).AddMinutes(durationMinutes),
        DurationMinutes = durationMinutes
    };

    private static string JiraIssueJson(string issueKey, int id) =>
        JsonSerializer.Serialize(new { id = id.ToString(), key = issueKey });

    private static string TempoWorklogsJson(params object[] worklogs) =>
        JsonSerializer.Serialize(new { results = worklogs });

    #region UploadWorklogAsync

    [Fact]
    public async Task UploadWorklogAsync_Success_ReturnsSuccess()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UploadWorklogAsync_EmptyTicketId_ReturnsFailure()
    {
        await InitializePluginAsync();

        var worklog = CreateWorklog();
        worklog.TicketId = "";
        var result = await _plugin.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Ticket ID");
    }

    [Fact]
    public async Task UploadWorklogAsync_NullTicketId_ReturnsFailure()
    {
        await InitializePluginAsync();

        var worklog = CreateWorklog();
        worklog.TicketId = null;
        var result = await _plugin.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Ticket ID");
    }

    [Fact]
    public async Task UploadWorklogAsync_ZeroDuration_ReturnsFailure()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        await InitializePluginAsync();

        var worklog = CreateWorklog(durationMinutes: 0);
        var result = await _plugin.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("duration");
    }

    [Fact]
    public async Task UploadWorklogAsync_NegativeDuration_ReturnsFailure()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        await InitializePluginAsync();

        var worklog = CreateWorklog(durationMinutes: -5);
        var result = await _plugin.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("duration");
    }

    [Fact]
    public async Task UploadWorklogAsync_IssueNotFound_ReturnsFailure()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/UNKNOWN-999?fields=id", HttpStatusCode.NotFound, """{"errorMessages":["Issue not found"]}""");
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog("UNKNOWN-999"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("UNKNOWN-999");
    }

    [Fact]
    public async Task UploadWorklogAsync_ClientError_NoRetry()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.BadRequest, "Bad request");
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _httpHandler.GetPostCallCount("/4/worklogs").Should().Be(1);
    }

    [Fact]
    public async Task UploadWorklogAsync_AllRetriesExhausted_ReturnsFailure()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPostSequence("/4/worklogs",
            (HttpStatusCode.BadGateway, "Bad gateway"),
            (HttpStatusCode.BadGateway, "Bad gateway"),
            (HttpStatusCode.BadGateway, "Bad gateway"));
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _httpHandler.GetPostCallCount("/4/worklogs").Should().Be(3);
    }

    [Fact]
    public async Task UploadWorklogAsync_NotInitialized_Throws()
    {
        var act = () => _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region GetWorklogsAsync

    [Fact]
    public async Task GetWorklogsAsync_NullStartDateInResponse_SkipsEntry()
    {
        var json = TempoWorklogsJson(new
        {
            issue = new { key = "PROJ-1" },
            startDate = (string?)null,
            startTime = "09:00:00",
            timeSpentSeconds = 3600,
            description = "Work"
        });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", json);
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorklogsAsync_Success_ReturnsParsedWorklogs()
    {
        var worklogsJson = TempoWorklogsJson(
            new
            {
                issue = new { key = "PROJ-1" },
                startDate = "2026-04-01",
                startTime = "09:00:00",
                timeSpentSeconds = 3600,
                description = "Work on task"
            },
            new
            {
                issue = new { key = "PROJ-2" },
                startDate = "2026-04-01",
                startTime = "14:00:00",
                timeSpentSeconds = 1800,
                description = "Review"
            });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var worklogs = result.Value!.ToList();
        worklogs.Should().HaveCount(2);

        worklogs[0].TicketId.Should().Be("PROJ-1");
        worklogs[0].DurationMinutes.Should().Be(60);
        worklogs[0].StartTime.Should().Be(new DateTime(2026, 4, 1, 9, 0, 0));
        worklogs[0].Description.Should().Be("Work on task");

        worklogs[1].TicketId.Should().Be("PROJ-2");
        worklogs[1].DurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task GetWorklogsAsync_EmptyResults_ReturnsEmptyList()
    {
        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", TempoWorklogsJson());
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorklogsAsync_MissingIssueProperty_SkipsEntry()
    {
        var json = TempoWorklogsJson(
            new { startDate = "2026-04-01", startTime = "09:00:00", timeSpentSeconds = 3600 });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", json);
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorklogsAsync_MissingKeyProperty_SkipsEntry()
    {
        var json = TempoWorklogsJson(
            new { issue = new { id = 123 }, startDate = "2026-04-01", startTime = "09:00:00", timeSpentSeconds = 3600 });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", json);
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorklogsAsync_HttpError_ReturnsFailure()
    {
        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01",
            HttpStatusCode.InternalServerError, "Server error");
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetWorklogsAsync_NotInitialized_Throws()
    {
        var act = () => _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region WorklogExistsAsync

    [Fact]
    public async Task WorklogExistsAsync_MatchingWorklog_ReturnsTrue()
    {
        var worklogsJson = TempoWorklogsJson(new
        {
            issue = new { key = "PROJ-123" },
            startDate = "2026-04-01",
            startTime = "09:00:00",
            timeSpentSeconds = 3600
        });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
        await InitializePluginAsync();

        var result = await _plugin.WorklogExistsAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task WorklogExistsAsync_NoMatch_ReturnsFalse()
    {
        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", TempoWorklogsJson());
        await InitializePluginAsync();

        var result = await _plugin.WorklogExistsAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task WorklogExistsAsync_TimeDifferenceUnderOneMinute_ReturnsTrue()
    {
        var worklogsJson = TempoWorklogsJson(new
        {
            issue = new { key = "PROJ-123" },
            startDate = "2026-04-01",
            startTime = "09:00:30",  // 30 seconds off
            timeSpentSeconds = 3600
        });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
        await InitializePluginAsync();

        var result = await _plugin.WorklogExistsAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task WorklogExistsAsync_TimeDifferenceOverOneMinute_ReturnsFalse()
    {
        var worklogsJson = TempoWorklogsJson(new
        {
            issue = new { key = "PROJ-123" },
            startDate = "2026-04-01",
            startTime = "09:02:00",  // 2 minutes off
            timeSpentSeconds = 3600
        });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
        await InitializePluginAsync();

        var result = await _plugin.WorklogExistsAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task WorklogExistsAsync_DifferentDuration_ReturnsFalse()
    {
        var worklogsJson = TempoWorklogsJson(new
        {
            issue = new { key = "PROJ-123" },
            startDate = "2026-04-01",
            startTime = "09:00:00",
            timeSpentSeconds = 1800  // 30 min, not 60
        });

        _httpHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
        await InitializePluginAsync();

        var result = await _plugin.WorklogExistsAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    #endregion

    #region TestConnectionAsync

    [Fact]
    public async Task TestConnectionAsync_Success_ReturnsSuccess()
    {
        _httpHandler.SetupGet("/rest/api/3/myself", """{"accountId":"abc"}""");
        await InitializePluginAsync();

        var result = await _plugin.TestConnectionAsync(null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_JiraError_ReturnsFailure()
    {
        _httpHandler.SetupGet("/rest/api/3/myself", HttpStatusCode.Unauthorized, "Unauthorized");
        await InitializePluginAsync();

        var result = await _plugin.TestConnectionAsync(null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_NotInitialized_Throws()
    {
        var act = () => _plugin.TestConnectionAsync(null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region SupportedModes / UploadWorklogsAsync

    [Fact]
    public void SupportedModes_AdvertisesBothTimedAndAggregated()
    {
        _plugin.SupportedModes.Should().Be(WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated);
    }

    [Fact]
    public async Task UploadWorklogsAsync_Aggregated_PostsPayloadWithoutStartTime()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-1?fields=id", JiraIssueJson("PROJ-1", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        // Simulate pre-aggregated input: one entry per (code, description, day).
        var aggregated = new[]
        {
            new PluginWorklogEntry
            {
                TicketId = "PROJ-1",
                Description = "Work",
                StartTime = new DateTime(2026, 4, 1, 9, 0, 0),
                EndTime = new DateTime(2026, 4, 1, 9, 0, 0),
                DurationMinutes = 150 // 2h30m = aggregated total (no end-start relationship)
            }
        };

        var result = await _plugin.UploadWorklogsAsync(aggregated, WorklogSubmissionMode.Aggregated, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuccessfulEntries.Should().Be(1);
        _httpHandler.GetPostCallCount("/4/worklogs").Should().Be(1);

        // Verify the payload: startTime must be absent, but startDate + timeSpentSeconds present
        var body = _httpHandler.GetPostBodies("/4/worklogs").Should().ContainSingle().Subject;
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.TryGetProperty("startTime", out _).Should().BeFalse("aggregated mode must omit startTime");
        json.GetProperty("startDate").GetString().Should().Be("2026-04-01");
        json.GetProperty("timeSpentSeconds").GetInt32().Should().Be(150 * 60);
        json.GetProperty("issueId").GetInt32().Should().Be(10001);
    }

    [Fact]
    public async Task UploadWorklogsAsync_Timed_PostsPayloadIncludingStartTime()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-1?fields=id", JiraIssueJson("PROJ-1", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        var timed = new[] { CreateWorklog("PROJ-1", 60, new DateTime(2026, 4, 1, 9, 30, 0)) };

        var result = await _plugin.UploadWorklogsAsync(timed, WorklogSubmissionMode.Timed, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var body = _httpHandler.GetPostBodies("/4/worklogs").Should().ContainSingle().Subject;
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.GetProperty("startTime").GetString().Should().Be("09:30:00");
        json.GetProperty("startDate").GetString().Should().Be("2026-04-01");
    }

    [Fact]
    public async Task UploadWorklogsAsync_Timed_PostsOnePerEntry()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-1?fields=id", JiraIssueJson("PROJ-1", 10001));
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-2?fields=id", JiraIssueJson("PROJ-2", 10002));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        var timed = new[]
        {
            CreateWorklog("PROJ-1"),
            CreateWorklog("PROJ-2")
        };

        var result = await _plugin.UploadWorklogsAsync(timed, WorklogSubmissionMode.Timed, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuccessfulEntries.Should().Be(2);
        _httpHandler.GetPostCallCount("/4/worklogs").Should().Be(2);
    }

    #endregion

    #region Issue ID caching

    [Fact]
    public async Task UploadWorklogAsync_SecondCall_UsesCachedIssueId()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        _httpHandler.GetGetCallCount("/rest/api/3/issue/PROJ-123?fields=id").Should().Be(1);
    }

    [Fact]
    public async Task UploadWorklogAsync_DifferentTickets_FetchesBoth()
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-1?fields=id", JiraIssueJson("PROJ-1", 10001));
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-2?fields=id", JiraIssueJson("PROJ-2", 10002));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        await _plugin.UploadWorklogAsync(CreateWorklog("PROJ-1"), CancellationToken.None);
        await _plugin.UploadWorklogAsync(CreateWorklog("PROJ-2"), CancellationToken.None);

        _httpHandler.GetGetCallCount("/rest/api/3/issue/PROJ-1?fields=id").Should().Be(1);
        _httpHandler.GetGetCallCount("/rest/api/3/issue/PROJ-2?fields=id").Should().Be(1);
    }

    #endregion

    #region Initialization

    [Fact]
    public async Task InitializeAsync_AutoDetectsAccountId()
    {
        var configWithoutAccountId = new Dictionary<string, string>(ValidConfig);
        configWithoutAccountId.Remove("JiraAccountId");

        _httpHandler.SetupGet("/rest/api/3/myself", """{"accountId":"auto-detected-id"}""");

        var plugin = new TempoWorklogPlugin(
            new MockHttpClientFactory(_httpHandler),
            NullLogger<TempoWorklogPlugin>.Instance)
        {
            RetryDelayStrategy = _ => TimeSpan.Zero
        };

        var result = await plugin.InitializeAsync(configWithoutAccountId, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        await plugin.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_ReInitialization_WorksCorrectly()
    {
        await InitializePluginAsync();

        // Re-initialize with the same config — should work without errors
        await InitializePluginAsync();

        // Plugin should still be functional
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_AutoDetectFails_ReturnsFalse()
    {
        var configWithoutAccountId = new Dictionary<string, string>(ValidConfig);
        configWithoutAccountId.Remove("JiraAccountId");

        _httpHandler.SetupGet("/rest/api/3/myself", """{"displayName":"Test User"}""");

        var plugin = new TempoWorklogPlugin(
            new MockHttpClientFactory(_httpHandler),
            NullLogger<TempoWorklogPlugin>.Instance)
        {
            RetryDelayStrategy = _ => TimeSpan.Zero
        };

        var result = await plugin.InitializeAsync(configWithoutAccountId, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        await plugin.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_FailedReInit_PreservesOldState()
    {
        // First init succeeds
        await InitializePluginAsync();

        // Upload works
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        var firstResult = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Second init fails (auto-detect returns no accountId)
        var badConfig = new Dictionary<string, string>(ValidConfig);
        badConfig.Remove("JiraAccountId");
        _httpHandler.SetupGet("/rest/api/3/myself", """{"displayName":"No Account Id"}""");
        var initResult = await _plugin.InitializeAsync(badConfig, TestContext.Current.CancellationToken);
        initResult.Should().BeFalse();

        // Plugin should still work with the old clients
        var secondResult = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        secondResult.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Retry status codes

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task UploadWorklogAsync_TransientErrors_RetriesAndSucceeds(HttpStatusCode transientCode)
    {
        _httpHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _httpHandler.SetupPostSequence("/4/worklogs",
            (transientCode, "Transient error"),
            (HttpStatusCode.OK, """{"tempoWorklogId": 1}"""));
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _httpHandler.GetPostCallCount("/4/worklogs").Should().Be(2);
    }

    #endregion
}

internal sealed class MockHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// Simple HTTP handler that returns pre-configured responses based on request URL and method.
/// </summary>
internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _getResponses = new();
    private readonly Dictionary<string, HttpStatusCode> _getStatusCodes = new();
    private readonly List<(string Prefix, string Body, HttpStatusCode Status)> _getPrefixResponses = new();
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _postSequences = new();
    private readonly HashSet<string> _postRepeatLast = new();
    private readonly Dictionary<string, int> _getCallCounts = new();
    private readonly Dictionary<string, int> _postCallCounts = new();
    private readonly Dictionary<string, List<string>> _postBodies = new();

    public void SetupGet(string pathAndQuery, string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _getResponses[pathAndQuery] = responseBody;
        _getStatusCodes[pathAndQuery] = statusCode;
    }

    public void SetupGet(string pathAndQuery, HttpStatusCode statusCode, string responseBody)
    {
        SetupGet(pathAndQuery, responseBody, statusCode);
    }

    public void SetupGetPrefix(string pathPrefix, string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _getPrefixResponses.Add((pathPrefix, responseBody, statusCode));
    }

    public void SetupGetPrefix(string pathPrefix, HttpStatusCode statusCode, string responseBody)
    {
        _getPrefixResponses.Add((pathPrefix, responseBody, statusCode));
    }

    public void SetupPost(string pathAndQuery, HttpStatusCode statusCode, string responseBody)
    {
        _postSequences[pathAndQuery] = new Queue<(HttpStatusCode, string)>(
            [(statusCode, responseBody)]);
        _postRepeatLast.Add(pathAndQuery);
    }

    public void SetupPostSequence(string pathAndQuery, params (HttpStatusCode Status, string Body)[] responses)
    {
        _postSequences[pathAndQuery] = new Queue<(HttpStatusCode, string)>(responses);
    }

    public int GetGetCallCount(string pathAndQuery) =>
        _getCallCounts.GetValueOrDefault(pathAndQuery, 0);

    public int GetPostCallCount(string pathAndQuery) =>
        _postCallCounts.GetValueOrDefault(pathAndQuery, 0);

    public IReadOnlyList<string> GetPostBodies(string pathAndQuery) =>
        _postBodies.TryGetValue(pathAndQuery, out var list) ? list : [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery;

        if (request.Method == HttpMethod.Get)
        {
            _getCallCounts[pathAndQuery] = _getCallCounts.GetValueOrDefault(pathAndQuery, 0) + 1;

            if (_getResponses.TryGetValue(pathAndQuery, out var body))
            {
                var statusCode = _getStatusCodes.GetValueOrDefault(pathAndQuery, HttpStatusCode.OK);
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                });
            }

            var prefixMatch = _getPrefixResponses.FirstOrDefault(p => pathAndQuery.StartsWith(p.Prefix, StringComparison.Ordinal));
            if (prefixMatch != default)
            {
                return Task.FromResult(new HttpResponseMessage(prefixMatch.Status)
                {
                    Content = new StringContent(prefixMatch.Body, System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Not found")
            });
        }

        if (request.Method == HttpMethod.Post)
        {
            _postCallCounts[pathAndQuery] = _postCallCounts.GetValueOrDefault(pathAndQuery, 0) + 1;

            if (request.Content != null)
            {
                var body = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                if (!_postBodies.TryGetValue(pathAndQuery, out var bodies))
                {
                    bodies = new List<string>();
                    _postBodies[pathAndQuery] = bodies;
                }
                bodies.Add(body);
            }

            if (_postSequences.TryGetValue(pathAndQuery, out var queue) && queue.Count > 0)
            {
                var (status, responseBody) = queue.Dequeue();
                if (queue.Count == 0 && _postRepeatLast.Contains(pathAndQuery))
                {
                    queue.Enqueue((status, responseBody));
                }

                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("No response configured")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
    }
}
