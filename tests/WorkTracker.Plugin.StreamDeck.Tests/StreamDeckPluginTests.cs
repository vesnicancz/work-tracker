using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck.Tests;

public sealed class StreamDeckPluginTests : IAsyncLifetime
{
	private readonly string _pipeName = $"wt-{Guid.NewGuid():N}"[..11];
	private readonly Mock<IRemoteControlHost> _host = new();
	private StreamDeckPlugin _plugin = null!;

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	public ValueTask InitializeAsync()
	{
		_host.Setup(h => h.GetStateAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RemoteControlState(
			null, null, null, new RemoteWorkedTime(TimeSpan.Zero, TimeSpan.Zero, false), new RemotePomodoroState(StatusIndicatorState.Idle, TimeSpan.Zero, 0, 4)));

		_plugin = new StreamDeckPlugin(NullLogger<StreamDeckPlugin>.Instance, _pipeName);
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
	}

	[Fact]
	public void PublicConstructor_IsTheOnlyOneThePluginLoaderCanUse()
	{
		typeof(StreamDeckPlugin).GetConstructors().Should().ContainSingle()
			.Which.GetParameters().Should().ContainSingle();
	}

	[Fact]
	public async Task Initialize_DoesNotOpenPipe()
	{
		// The CLI initializes plugins too, and must not start listening
		await _plugin.InitializeAsync(null, TestToken);

		(await CanConnectAsync()).Should().BeFalse();
	}

	[Fact]
	public async Task Attach_OpensPipe()
	{
		await _plugin.AttachAsync(_host.Object, TestToken);

		(await CanConnectAsync()).Should().BeTrue();
	}

	[Fact]
	public async Task Attach_Twice_KeepsServing()
	{
		await _plugin.AttachAsync(_host.Object, TestToken);
		await _plugin.AttachAsync(_host.Object, TestToken);

		(await CanConnectAsync()).Should().BeTrue();
	}

	[Fact]
	public async Task Shutdown_ClosesPipe()
	{
		await _plugin.AttachAsync(_host.Object, TestToken);

		await _plugin.ShutdownAsync();

		(await CanConnectAsync()).Should().BeFalse();
	}

	[Fact]
	public async Task Shutdown_WhenNeverAttached_DoesNothing()
	{
		await _plugin.Invoking(p => p.ShutdownAsync()).Should().NotThrowAsync();
	}

	[Fact]
	public async Task ResumeWindow_DefaultsToSevenDays()
	{
		await _plugin.InitializeAsync(null, TestToken);
		await _plugin.AttachAsync(_host.Object, TestToken);

		await ConnectAndReadStateAsync();

		_host.Verify(h => h.GetStateAsync(TimeSpan.FromDays(7), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ResumeWindow_ComesFromConfiguration()
	{
		await _plugin.InitializeAsync(new Dictionary<string, string> { ["resume_window_days"] = "3" }, TestToken);
		await _plugin.AttachAsync(_host.Object, TestToken);

		await ConnectAndReadStateAsync();

		_host.Verify(h => h.GetStateAsync(TimeSpan.FromDays(3), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task DailyTarget_DefaultsToEightHours()
	{
		await _plugin.InitializeAsync(new Dictionary<string, string>(), TestToken);
		await _plugin.AttachAsync(_host.Object, TestToken);

		var state = await ConnectAndReadStateAsync();

		state.GetProperty("workedTime").GetProperty("dailyTargetSeconds").GetInt32().Should().Be(8 * 3600);
	}

	[Fact]
	public async Task DailyTarget_ComesFromConfiguration()
	{
		await _plugin.InitializeAsync(new Dictionary<string, string> { ["daily_target_hours"] = "7.5" }, TestToken);
		await _plugin.AttachAsync(_host.Object, TestToken);

		var state = await ConnectAndReadStateAsync();

		state.GetProperty("workedTime").GetProperty("dailyTargetSeconds").GetInt32().Should().Be(27000);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("0.5")]
	[InlineData("24.5")]
	[InlineData("25")]
	[InlineData("7,5")]
	[InlineData("abc")]
	public async Task Validate_RejectsInvalidDailyTarget(string value)
	{
		var result = await _plugin.ValidateConfigurationAsync(new Dictionary<string, string> { ["daily_target_hours"] = value }, TestToken);

		result.IsValid.Should().BeFalse();
	}

	[Theory]
	[InlineData("1")]
	[InlineData("7.5")]
	[InlineData("8")]
	[InlineData("24")]
	[InlineData("")]
	public async Task Validate_AcceptsValidOrEmptyDailyTarget(string value)
	{
		var result = await _plugin.ValidateConfigurationAsync(new Dictionary<string, string> { ["daily_target_hours"] = value }, TestToken);

		result.IsValid.Should().BeTrue();
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1.5")]
	[InlineData("1000")]
	[InlineData("abc")]
	public async Task Validate_RejectsInvalidWindow(string value)
	{
		var result = await _plugin.ValidateConfigurationAsync(new Dictionary<string, string> { ["resume_window_days"] = value }, TestToken);

		result.IsValid.Should().BeFalse();
	}

	[Theory]
	[InlineData("1")]
	[InlineData("999")]
	[InlineData("")]
	public async Task Validate_AcceptsValidOrEmptyWindow(string value)
	{
		var result = await _plugin.ValidateConfigurationAsync(new Dictionary<string, string> { ["resume_window_days"] = value }, TestToken);

		result.IsValid.Should().BeTrue();
	}

	private async Task<JsonElement> ConnectAndReadStateAsync()
	{
		await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		await client.ConnectAsync(TestToken);
		using var reader = new StreamReader(client, leaveOpen: true);
		var line = await reader.ReadLineAsync(TestToken);
		return JsonDocument.Parse(line!).RootElement.Clone();
	}

	private async Task<bool> CanConnectAsync()
	{
		await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		try
		{
			await client.ConnectAsync(500, TestToken);
			return true;
		}
		catch (TimeoutException)
		{
			return false;
		}
	}
}
