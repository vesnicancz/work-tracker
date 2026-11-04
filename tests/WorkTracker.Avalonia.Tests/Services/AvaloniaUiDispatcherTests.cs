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
		var dispatcher = new AvaloniaUiDispatcher();
		var ranOnUiThread = false;

		// The timeout turns a dispatcher that stops responding into a failure instead of a hung test run
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(30));

		// The headless UI thread runs its queue only during a dispatch to the session, so the call from
		// the background thread has to wait inside one. Outside, it finishes only if another test happens
		// to be dispatching at that moment.
		await UiThread.Dispatch(async () =>
		{
			ranOnUiThread = await Task.Run(
				() => dispatcher.InvokeAsync(() => Task.FromResult(Dispatcher.UIThread.CheckAccess()), timeout.Token),
				timeout.Token);
		});

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
