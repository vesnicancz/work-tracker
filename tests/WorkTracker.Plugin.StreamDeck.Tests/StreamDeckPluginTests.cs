using System.IO.Pipes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck.Tests;

public sealed class StreamDeckPluginTests : IAsyncLifetime
{
	private readonly string _pipeName = $"worktracker-test-{Guid.NewGuid():N}";
	private readonly Mock<IRemoteControlHost> _host = new();
	private StreamDeckPlugin _plugin = null!;

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	public ValueTask InitializeAsync()
	{
		_host.Setup(h => h.GetStateAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RemoteControlState(
			null, null, new RemotePomodoroState(StatusIndicatorState.Idle, TimeSpan.Zero, 0, 4)));

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
	public async Task Detach_ClosesPipe()
	{
		await _plugin.AttachAsync(_host.Object, TestToken);

		await _plugin.DetachAsync();

		(await CanConnectAsync()).Should().BeFalse();
	}

	[Fact]
	public async Task Attach_AfterDetach_OpensPipeAgain()
	{
		await _plugin.AttachAsync(_host.Object, TestToken);
		await _plugin.DetachAsync();

		await _plugin.AttachAsync(_host.Object, TestToken);

		(await CanConnectAsync()).Should().BeTrue();
	}

	[Fact]
	public async Task Detach_WhenNeverAttached_DoesNothing()
	{
		await _plugin.Invoking(p => p.DetachAsync()).Should().NotThrowAsync();
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
	public async Task Reinitialize_WithNewWindow_PushesStateToConnectedClient()
	{
		await _plugin.InitializeAsync(new Dictionary<string, string> { ["resume_window_days"] = "3" }, TestToken);
		await _plugin.AttachAsync(_host.Object, TestToken);
		await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		await client.ConnectAsync(TestToken);
		using var reader = new StreamReader(client, leaveOpen: true);
		await reader.ReadLineAsync(TestToken);

		await _plugin.InitializeAsync(new Dictionary<string, string> { ["resume_window_days"] = "14" }, TestToken);
		await reader.ReadLineAsync(TestToken);

		_host.Verify(h => h.GetStateAsync(TimeSpan.FromDays(14), It.IsAny<CancellationToken>()), Times.Once);
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

	private async Task ConnectAndReadStateAsync()
	{
		await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		await client.ConnectAsync(TestToken);
		using var reader = new StreamReader(client, leaveOpen: true);
		await reader.ReadLineAsync(TestToken);
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
