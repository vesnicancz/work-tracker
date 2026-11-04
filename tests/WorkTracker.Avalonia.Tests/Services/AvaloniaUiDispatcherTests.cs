using Avalonia.Threading;
using FluentAssertions;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class AvaloniaUiDispatcherTests
{
	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	[Fact]
	public async Task InvokeAsync_FromBackgroundThread_RunsOnUiThreadAndReturnsResult()
	{
		// Starts the headless session this test depends on
		await UiThread.Dispatch(() => { });
		var dispatcher = new AvaloniaUiDispatcher();

		var ranOnUiThread = await Task.Run(
			() => dispatcher.InvokeAsync(() => Task.FromResult(Dispatcher.UIThread.CheckAccess()), TestToken),
			TestToken);

		ranOnUiThread.Should().BeTrue();
	}

	[Fact]
	public async Task InvokeAsync_WhenCancelled_StopsWaitingForTheUiThread()
	{
		await UiThread.Dispatch(() => { });
		var dispatcher = new AvaloniaUiDispatcher();
		var neverFinishes = new TaskCompletionSource<bool>();
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestToken);

		var waiting = Task.Run(() => dispatcher.InvokeAsync(() => neverFinishes.Task, cancellation.Token), TestToken);
		await cancellation.CancelAsync();

		await waiting.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();
	}
}
