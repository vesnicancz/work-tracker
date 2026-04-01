using System.Net;
using System.Text.Json;
using FluentAssertions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Atlassian.Tests;

public class TempoWorklogPluginTests : IDisposable
{
    private readonly MockHttpHandler _tempoHandler = new();
    private readonly MockHttpHandler _jiraHandler = new();
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
        _plugin = new TempoWorklogPlugin
        {
            TempoHttpHandler = _tempoHandler,
            JiraHttpHandler = _jiraHandler,
            RetryDelayStrategy = _ => TimeSpan.Zero
        };
    }

    public void Dispose()
    {
        _plugin.Dispose();
        _tempoHandler.Dispose();
        _jiraHandler.Dispose();
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
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
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
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        await InitializePluginAsync();

        var worklog = CreateWorklog(durationMinutes: 0);
        var result = await _plugin.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("duration");
    }

    [Fact]
    public async Task UploadWorklogAsync_NegativeDuration_ReturnsFailure()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        await InitializePluginAsync();

        var worklog = CreateWorklog(durationMinutes: -5);
        var result = await _plugin.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("duration");
    }

    [Fact]
    public async Task UploadWorklogAsync_IssueNotFound_ReturnsFailure()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/UNKNOWN-999?fields=id", HttpStatusCode.NotFound, """{"errorMessages":["Issue not found"]}""");
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog("UNKNOWN-999"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("UNKNOWN-999");
    }

    [Fact]
    public async Task UploadWorklogAsync_ServerError_RetriesAndSucceeds()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPostSequence("/4/worklogs",
            (HttpStatusCode.InternalServerError, "Server error"),
            (HttpStatusCode.OK, """{"tempoWorklogId": 1}"""));
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tempoHandler.GetPostCallCount("/4/worklogs").Should().Be(2);
    }

    [Fact]
    public async Task UploadWorklogAsync_TooManyRequests_RetriesAndSucceeds()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPostSequence("/4/worklogs",
            (HttpStatusCode.TooManyRequests, "Rate limited"),
            (HttpStatusCode.OK, """{"tempoWorklogId": 1}"""));
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UploadWorklogAsync_RequestTimeout_RetriesAndSucceeds()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPostSequence("/4/worklogs",
            (HttpStatusCode.RequestTimeout, "Request timeout"),
            (HttpStatusCode.OK, """{"tempoWorklogId": 1}"""));
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UploadWorklogAsync_ClientError_NoRetry()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPost("/4/worklogs", HttpStatusCode.BadRequest, "Bad request");
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _tempoHandler.GetPostCallCount("/4/worklogs").Should().Be(1);
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
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
        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", TempoWorklogsJson());
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", json);
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", json);
        await InitializePluginAsync();

        var result = await _plugin.GetWorklogsAsync(
            new DateTime(2026, 4, 1), new DateTime(2026, 4, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorklogsAsync_HttpError_ReturnsFailure()
    {
        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01",
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
        await InitializePluginAsync();

        var result = await _plugin.WorklogExistsAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task WorklogExistsAsync_NoMatch_ReturnsFalse()
    {
        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", TempoWorklogsJson());
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
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

        _tempoHandler.SetupGet("/4/worklogs?from=2026-04-01&to=2026-04-01", worklogsJson);
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
        _jiraHandler.SetupGet("/rest/api/3/myself", """{"accountId":"abc"}""");
        await InitializePluginAsync();

        var result = await _plugin.TestConnectionAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_JiraError_ReturnsFailure()
    {
        _jiraHandler.SetupGet("/rest/api/3/myself", HttpStatusCode.Unauthorized, "Unauthorized");
        await InitializePluginAsync();

        var result = await _plugin.TestConnectionAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_NotInitialized_Throws()
    {
        var act = () => _plugin.TestConnectionAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Issue ID caching

    [Fact]
    public async Task UploadWorklogAsync_SecondCall_UsesCachedIssueId()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        _jiraHandler.GetGetCallCount("/rest/api/3/issue/PROJ-123?fields=id").Should().Be(1);
    }

    [Fact]
    public async Task UploadWorklogAsync_DifferentTickets_FetchesBoth()
    {
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-1?fields=id", JiraIssueJson("PROJ-1", 10001));
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-2?fields=id", JiraIssueJson("PROJ-2", 10002));
        _tempoHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        await InitializePluginAsync();

        await _plugin.UploadWorklogAsync(CreateWorklog("PROJ-1"), CancellationToken.None);
        await _plugin.UploadWorklogAsync(CreateWorklog("PROJ-2"), CancellationToken.None);

        _jiraHandler.GetGetCallCount("/rest/api/3/issue/PROJ-1?fields=id").Should().Be(1);
        _jiraHandler.GetGetCallCount("/rest/api/3/issue/PROJ-2?fields=id").Should().Be(1);
    }

    #endregion

    #region Initialization

    [Fact]
    public async Task InitializeAsync_AutoDetectsAccountId()
    {
        var configWithoutAccountId = new Dictionary<string, string>(ValidConfig);
        configWithoutAccountId.Remove("JiraAccountId");

        _jiraHandler.SetupGet("/rest/api/3/myself", """{"accountId":"auto-detected-id"}""");

        var plugin = new TempoWorklogPlugin
        {
            TempoHttpHandler = _tempoHandler,
            JiraHttpHandler = _jiraHandler,
            RetryDelayStrategy = _ => TimeSpan.Zero
        };

        var result = await plugin.InitializeAsync(configWithoutAccountId, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        plugin.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_ReInitialization_WorksCorrectly()
    {
        await InitializePluginAsync();

        // Re-initialize with the same config — should work without errors
        await InitializePluginAsync();

        // Plugin should still be functional
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_AutoDetectFails_ReturnsFalse()
    {
        var configWithoutAccountId = new Dictionary<string, string>(ValidConfig);
        configWithoutAccountId.Remove("JiraAccountId");

        _jiraHandler.SetupGet("/rest/api/3/myself", """{"displayName":"Test User"}""");

        var plugin = new TempoWorklogPlugin
        {
            TempoHttpHandler = _tempoHandler,
            JiraHttpHandler = _jiraHandler,
            RetryDelayStrategy = _ => TimeSpan.Zero
        };

        var result = await plugin.InitializeAsync(configWithoutAccountId, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        plugin.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_FailedReInit_PreservesOldState()
    {
        // First init succeeds
        await InitializePluginAsync();

        // Upload works
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPost("/4/worklogs", HttpStatusCode.OK, """{"tempoWorklogId": 1}""");
        var firstResult = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Second init fails (auto-detect returns no accountId)
        var badConfig = new Dictionary<string, string>(ValidConfig);
        badConfig.Remove("JiraAccountId");
        _jiraHandler.SetupGet("/rest/api/3/myself", """{"displayName":"No Account Id"}""");
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
        _jiraHandler.SetupGet("/rest/api/3/issue/PROJ-123?fields=id", JiraIssueJson("PROJ-123", 10001));
        _tempoHandler.SetupPostSequence("/4/worklogs",
            (transientCode, "Transient error"),
            (HttpStatusCode.OK, """{"tempoWorklogId": 1}"""));
        await InitializePluginAsync();

        var result = await _plugin.UploadWorklogAsync(CreateWorklog(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tempoHandler.GetPostCallCount("/4/worklogs").Should().Be(2);
    }

    #endregion
}

/// <summary>
/// Simple HTTP handler that returns pre-configured responses based on request URL and method.
/// </summary>
internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _getResponses = new();
    private readonly Dictionary<string, HttpStatusCode> _getStatusCodes = new();
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _postSequences = new();
    private readonly HashSet<string> _postRepeatLast = new();
    private readonly Dictionary<string, int> _getCallCounts = new();
    private readonly Dictionary<string, int> _postCallCounts = new();

    public void SetupGet(string pathAndQuery, string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _getResponses[pathAndQuery] = responseBody;
        _getStatusCodes[pathAndQuery] = statusCode;
    }

    public void SetupGet(string pathAndQuery, HttpStatusCode statusCode, string responseBody)
    {
        SetupGet(pathAndQuery, responseBody, statusCode);
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

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("Not found")
            });
        }

        if (request.Method == HttpMethod.Post)
        {
            _postCallCounts[pathAndQuery] = _postCallCounts.GetValueOrDefault(pathAndQuery, 0) + 1;

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
