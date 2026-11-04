using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck.Tests;

public sealed class RemoteControlPipeServerTests : IAsyncLifetime
{
	private static readonly RemoteWorkItem Work = new("PROJ-1", "Coding", new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.FromHours(1)));

	private static readonly RemotePomodoroState Pomodoro = new(StatusIndicatorState.ShortBreak, TimeSpan.FromSeconds(90), 2, 4);

	private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

	private readonly string _pipeName = $"worktracker-test-{Guid.NewGuid():N}";
	private readonly Mock<IRemoteControlHost> _host = new();
	private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
	private RemoteControlPipeServer _server = null!;
	private NamedPipeClientStream _client = null!;
	private StreamReader _reader = null!;

	public async ValueTask InitializeAsync()
	{
		_timeout.CancelAfter(TimeSpan.FromSeconds(30));
		SetState(new RemoteControlState(Work, null, Pomodoro));

		_server = new RemoteControlPipeServer(_pipeName, _host.Object, () => MaxAge, NullLogger.Instance);
		_server.Start();
		(_client, _reader) = await ConnectAsync();
	}

	public async ValueTask DisposeAsync()
	{
		_reader.Dispose();
		await _client.DisposeAsync();
		await _server.DisposeAsync();
		_timeout.Dispose();
	}

	[Fact]
	public async Task Connect_PushesCurrentState()
	{
		var state = await ReadMessageAsync();

		state.GetProperty("type").GetString().Should().Be("state");

		var activeWork = state.GetProperty("activeWork");
		activeWork.GetProperty("ticketId").GetString().Should().Be("PROJ-1");
		activeWork.GetProperty("description").GetString().Should().Be("Coding");
		activeWork.GetProperty("startedAt").GetDateTimeOffset().Should().Be(Work.StartedAt);
		state.GetProperty("lastWork").ValueKind.Should().Be(JsonValueKind.Null);

		var pomodoro = state.GetProperty("pomodoro");
		pomodoro.GetProperty("phase").GetString().Should().Be("shortBreak");
		pomodoro.GetProperty("remainingSeconds").GetInt32().Should().Be(90);
		pomodoro.GetProperty("completedPomodoros").GetInt32().Should().Be(2);
		pomodoro.GetProperty("pomodorosBeforeLongBreak").GetInt32().Should().Be(4);
	}

	[Fact]
	public async Task StateChanged_PushesNewState()
	{
		await ReadMessageAsync();
		SetState(new RemoteControlState(null, Work, Pomodoro));

		_host.Raise(h => h.StateChanged += null, EventArgs.Empty);

		var state = await ReadMessageAsync();
		state.GetProperty("activeWork").ValueKind.Should().Be(JsonValueKind.Null);
		state.GetProperty("lastWork").GetProperty("ticketId").GetString().Should().Be("PROJ-1");
	}

	[Fact]
	public async Task RefreshState_PushesState()
	{
		await ReadMessageAsync();
		SetState(new RemoteControlState(null, Work, Pomodoro));

		_server.RefreshState();

		var state = await ReadMessageAsync();
		state.GetProperty("lastWork").GetProperty("ticketId").GetString().Should().Be("PROJ-1");
	}

	[Fact]
	public async Task ToggleWork_WhileTracking_StopsWork()
	{
		_host.Setup(h => h.StopWorkAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		var response = await SendAsync("""{"id":7,"command":"toggleWork"}""");

		response.GetProperty("id").GetInt64().Should().Be(7);
		response.GetProperty("success").GetBoolean().Should().BeTrue();
		_host.Verify(h => h.StopWorkAsync(It.IsAny<CancellationToken>()), Times.Once);
		_host.Verify(h => h.ResumeLastWorkAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task ToggleWork_WhileIdle_ResumesLastWork()
	{
		SetState(new RemoteControlState(null, Work, Pomodoro));
		_host.Setup(h => h.ResumeLastWorkAsync(MaxAge, It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		var response = await SendAsync("""{"id":1,"command":"toggleWork"}""");

		response.GetProperty("success").GetBoolean().Should().BeTrue();
		_host.Verify(h => h.ResumeLastWorkAsync(MaxAge, It.IsAny<CancellationToken>()), Times.Once);
		_host.Verify(h => h.StopWorkAsync(It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task HostFailure_IsReportedToClient()
	{
		_host.Setup(h => h.StopWorkAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Failure("Nope."));

		var response = await SendAsync("""{"id":1,"command":"stopWork"}""");

		response.GetProperty("success").GetBoolean().Should().BeFalse();
		response.GetProperty("error").GetString().Should().Be("Nope.");
	}

	[Fact]
	public async Task HostException_IsReportedToClientAndConnectionSurvives()
	{
		_host.Setup(h => h.StopWorkAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Boom."));
		_host.Setup(h => h.StartPomodoroAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		var failed = await SendAsync("""{"id":1,"command":"stopWork"}""");
		var succeeded = await SendAsync("""{"id":2,"command":"startPomodoro"}""");

		failed.GetProperty("error").GetString().Should().Be("Boom.");
		succeeded.GetProperty("success").GetBoolean().Should().BeTrue();
	}

	[Fact]
	public async Task ShiftStart_PassesMinutesToHost()
	{
		_host.Setup(h => h.ShiftActiveWorkStartAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		await SendAsync("""{"id":1,"command":"shiftStart","minutes":-5}""");

		_host.Verify(h => h.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StartWork_PassesTicketAndDescriptionToHost()
	{
		_host.Setup(h => h.StartWorkAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		await SendAsync("""{"id":1,"command":"startWork","ticketId":"PROJ-2","description":"Review"}""");

		_host.Verify(h => h.StartWorkAsync("PROJ-2", "Review", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task AddWork_PassesTimesToHost()
	{
		var start = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(1));
		_host.Setup(h => h.AddWorkEntryAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		var response = await SendAsync("""{"id":1,"command":"addWork","description":"Standup","startTime":"2026-11-04T10:00:00+01:00","endTime":"2026-11-04T10:30:00+01:00"}""");

		response.GetProperty("success").GetBoolean().Should().BeTrue();
		_host.Verify(h => h.AddWorkEntryAsync(null, "Standup", start, start.AddMinutes(30), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task AddWork_WithoutStartTime_FailsWithoutCallingHost()
	{
		var response = await SendAsync("""{"id":1,"command":"addWork","description":"Standup"}""");

		response.GetProperty("success").GetBoolean().Should().BeFalse();
		_host.Verify(h => h.AddWorkEntryAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetSuggestions_ReturnsThemInTheResponse()
	{
		var start = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(1));
		_host.Setup(h => h.GetSuggestionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RemoteSuggestions(
			[new RemoteSuggestion("Standup", null, start, start.AddMinutes(15), "O365 Calendar"), new RemoteSuggestion("Fix login", "PROJ-1", null, null, "Jira")],
			["GitHub"]));

		var response = await SendAsync("""{"id":3,"command":"getSuggestions"}""");

		response.GetProperty("id").GetInt64().Should().Be(3);
		response.GetProperty("success").GetBoolean().Should().BeTrue();

		var suggestions = response.GetProperty("suggestions");
		suggestions.GetProperty("failedSources")[0].GetString().Should().Be("GitHub");

		var items = suggestions.GetProperty("items");
		items[0].GetProperty("title").GetString().Should().Be("Standup");
		items[0].GetProperty("startTime").GetDateTimeOffset().Should().Be(start);
		items[0].GetProperty("endTime").GetDateTimeOffset().Should().Be(start.AddMinutes(15));
		items[0].GetProperty("source").GetString().Should().Be("O365 Calendar");
		items[1].GetProperty("ticketId").GetString().Should().Be("PROJ-1");
		items[1].GetProperty("startTime").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task GetSuggestions_HostException_IsReportedToClient()
	{
		_host.Setup(h => h.GetSuggestionsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException("Too slow."));

		var response = await SendAsync("""{"id":1,"command":"getSuggestions"}""");

		response.GetProperty("success").GetBoolean().Should().BeFalse();
		response.GetProperty("error").GetString().Should().Be("Too slow.");
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("""{"id":1,"command":"selfDestruct"}""")]
	[InlineData("""{"id":1}""")]
	public async Task InvalidRequest_ReportsFailureAndKeepsConnection(string line)
	{
		_host.Setup(h => h.StartPomodoroAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PluginResult.Success());

		var failed = await SendAsync(line);
		var succeeded = await SendAsync("""{"id":2,"command":"startPomodoro"}""");

		failed.GetProperty("success").GetBoolean().Should().BeFalse();
		succeeded.GetProperty("success").GetBoolean().Should().BeTrue();
	}

	[Fact]
	public async Task ClientReconnect_IsServedAgainWithCurrentState()
	{
		await ReadMessageAsync();

		_reader.Dispose();
		await _client.DisposeAsync();
		(_client, _reader) = await ConnectAsync();

		var state = await ReadMessageAsync();
		state.GetProperty("type").GetString().Should().Be("state");
	}

	[Fact]
	public async Task Dispose_DisconnectsClient()
	{
		await ReadMessageAsync();

		await _server.DisposeAsync();
		_server = new RemoteControlPipeServer(_pipeName + "-unused", _host.Object, () => MaxAge, NullLogger.Instance);

		var line = await _reader.ReadLineAsync(_timeout.Token);
		line.Should().BeNull();
	}

	private void SetState(RemoteControlState state)
	{
		_host.Setup(h => h.GetStateAsync(MaxAge, It.IsAny<CancellationToken>())).ReturnsAsync(state);
	}

	private async Task<(NamedPipeClientStream Client, StreamReader Reader)> ConnectAsync()
	{
		var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		await client.ConnectAsync(_timeout.Token);
		return (client, new StreamReader(client, leaveOpen: true));
	}

	/// <summary>
	/// Sends a raw line and returns its response, skipping the state pushes in between.
	/// </summary>
	private async Task<JsonElement> SendAsync(string line)
	{
		await using var writer = new StreamWriter(_client, leaveOpen: true) { NewLine = "\n" };
		await writer.WriteLineAsync(line.AsMemory(), _timeout.Token);
		await writer.FlushAsync(_timeout.Token);

		while (true)
		{
			var message = await ReadMessageAsync();
			if (message.GetProperty("type").GetString() == "result")
			{
				return message;
			}
		}
	}

	private async Task<JsonElement> ReadMessageAsync()
	{
		var line = await _reader.ReadLineAsync(_timeout.Token);
		line.Should().NotBeNull("the server should still be connected");
		return JsonSerializer.Deserialize<JsonElement>(line!);
	}
}
