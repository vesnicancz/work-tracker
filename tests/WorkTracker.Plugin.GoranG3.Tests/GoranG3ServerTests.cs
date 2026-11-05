using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3.Tests;

/// <summary>
/// The whole plugin with the real MCP client against <see cref="FakeGoranServer"/>: where the calls go,
/// and what happens when the server or the sign-in comes and goes.
/// </summary>
public sealed class GoranG3ServerTests : IAsyncDisposable
{
	private const string Create = "create_my_timesheet_item";
	private const string Submit = "submit_my_timesheet";

	private static readonly DateTime Monday = new(2026, 11, 2);
	private static readonly DateTime Tuesday = Monday.AddDays(1);

	private readonly FakeGoranServer _server = new();
	private readonly SwitchableTokenProvider _tokenProvider = new() { Token = "token" };
	private readonly GoranG3WorklogPlugin _plugin;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public GoranG3ServerTests()
	{
		_plugin = CreatePlugin();
	}

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
	}

	private GoranG3WorklogPlugin CreatePlugin() =>
		new(NullLogger<GoranG3WorklogPlugin>.Instance, new SingleTokenProviderFactory(_tokenProvider))
		{
			InnerHandlerFactory = _server.CreateHandler
		};

	private static Dictionary<string, string> ConfigFor(string url) => new()
	{
		["GoranBaseUrl"] = url,
		["ProjectCode"] = "000.GOR",
		["EntraClientId"] = "00000000-0000-0000-0000-000000000000",
		["EntraTenantId"] = "00000000-0000-0000-0000-000000000001",
		["EntraScopes"] = "api://test/Mcp.Access"
	};

	private static PluginWorklogEntry Entry(DateTime day, string ticket, int hour = 9) => new()
	{
		TicketId = ticket,
		StartTime = day.AddHours(hour),
		EndTime = day.AddHours(hour + 1),
		DurationMinutes = 60
	};

	private Task<PluginResult<WorklogSubmissionResult>> SendAsync(params PluginWorklogEntry[] worklogs) =>
		_plugin.UploadWorklogsAsync(worklogs, WorklogSubmissionMode.Timed, Token);

	private Task InitializeAsync(string url = "https://goran.test") =>
		_plugin.InitializeAsync(ConfigFor(url), Token);

	#region Where the calls go

	[Fact]
	public async Task Upload_CallsTheConfiguredServerWithTheToken()
	{
		await InitializeAsync("https://goran.test");

		var result = await SendAsync(Entry(Monday, "A-1"));

		result.Value!.SuccessfulEntries.Should().Be(1);
		_server.ToolCalls.Should().OnlyContain(c => c.Host == "goran.test" && c.Authorization == "token");
		_server.CallsOf(Create).Should().ContainSingle();
		_server.CallsOf(Submit).Should().ContainSingle();
	}

	[Fact]
	public async Task TestConnection_TellsWhichServerItConnectedTo()
	{
		_tokenProvider.Token = null;
		await InitializeAsync("https://goran.test");
		_tokenProvider.Token = "token";
		var progress = new List<string>();

		await _plugin.TestConnectionAsync(new SynchronousProgress(progress), Token);

		progress.Should().Contain(message => message.Contains("https://goran.test/mcp"));
	}

	#endregion

	#region Coming back without a restart

	[Fact]
	public async Task ServerDownAtStartup_UploadWorksOnceItIsBack()
	{
		_server.IsDown = true;
		await InitializeAsync();

		(await SendAsync(Entry(Monday, "A-1"))).IsFailure.Should().BeTrue("the server is still down");

		_server.IsDown = false;
		var result = await SendAsync(Entry(Monday, "A-1"));

		result.Value!.SuccessfulEntries.Should().Be(1);
		_server.CallsOf(Submit).Should().ContainSingle();
	}

	[Fact]
	public async Task ConnectionBreaksDuringAnUpload_NextUploadReconnects()
	{
		await InitializeAsync();
		await SendAsync(Entry(Monday, "A-1"));

		_server.IsDown = true;
		var failed = await SendAsync(Entry(Tuesday, "B-1"));
		_server.IsDown = false;
		var retried = await SendAsync(Entry(Tuesday, "B-1"));

		failed.IsFailure.Should().BeFalse();
		failed.Value!.FailedEntries.Should().Be(1);
		retried.Value!.SuccessfulEntries.Should().Be(1);
		_server.CallsOf(Create).Should().HaveCount(2);
	}

	[Fact]
	public async Task SignedInOnAnotherInstance_RunningPluginConnectsOnItsNextUpload()
	{
		// What a connection test in Settings does: it signs in on its own instance, and the token
		// reaches the running plugin through the shared cache.
		_tokenProvider.Token = null;
		await InitializeAsync();

		await using (var tested = CreatePlugin())
		{
			_tokenProvider.Token = "token";
			await tested.InitializeAsync(ConfigFor("https://goran.test"), Token);
			(await tested.TestConnectionAsync(null, Token)).IsSuccess.Should().BeTrue();
		}

		var result = await SendAsync(Entry(Monday, "A-1"));

		result.Value!.SuccessfulEntries.Should().Be(1);
	}

	#endregion

	#region Closing days

	[Fact]
	public async Task FailedEntry_IsFixedAndRetried_ItsDayClosesAndOtherDaysAreNotTouched()
	{
		await InitializeAsync();
		_server.RejectedTexts.Add("A-BAD");

		var first = await SendAsync(Entry(Monday, "A-1"), Entry(Monday, "A-BAD", hour: 10), Entry(Tuesday, "B-1"));

		first.Value!.FailedEntries.Should().Be(1);
		first.Value.Errors.Should().ContainSingle().Which.WorklogIndex.Should().Be(1);
		_server.CallsOf(Submit).Select(c => c.Arguments["date"]!.GetValue<string>()).Should().Equal("2026-11-03");

		var retried = await SendAsync(Entry(Monday, "A-2", hour: 10));

		retried.Value!.FailedEntries.Should().Be(0);
		retried.Value.Errors.Should().BeEmpty();
		_server.CallsOf(Submit).Select(c => c.Arguments["date"]!.GetValue<string>()).Should().Equal("2026-11-03", "2026-11-02");
		_server.CallsOf(Create).Select(c => c.Arguments["text"]!.GetValue<string>()).Should().Equal("A-1", "A-BAD", "B-1", "A-2");
	}

	[Fact]
	public async Task SubmitFailed_Retry_ClosesTheDayWithoutCreatingTheEntryAgain()
	{
		await InitializeAsync();
		_server.RejectsSubmit = true;
		var first = await SendAsync(Entry(Monday, "A-1"));
		first.Value!.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("Timesheet submit failed");

		_server.RejectsSubmit = false;
		var retried = await SendAsync(Entry(Monday, "A-1"));

		retried.Value!.Errors.Should().BeEmpty();
		_server.CallsOf(Create).Should().ContainSingle();
		_server.CallsOf(Submit).Should().HaveCount(2);
	}

	#endregion

	private sealed class SynchronousProgress(List<string> messages) : IProgress<string>
	{
		public void Report(string value) => messages.Add(value);
	}
}
