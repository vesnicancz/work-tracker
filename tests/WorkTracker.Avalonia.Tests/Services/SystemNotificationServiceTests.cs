using Avalonia.Labs.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class SystemNotificationServiceTests
{
	/// <summary>
	/// Stands in for the platform manager and reproduces what the Linux one does: away from the UI
	/// thread it returns null instead of throwing, so a caller that does not marshal first is left
	/// with nothing to show and no error to report.
	/// </summary>
	private sealed class ThreadAffineManager : INativeNotificationManager
	{
		private readonly Dictionary<uint, INativeNotification> _active = new();

		public bool? CreatedOnUiThread { get; private set; }
		public int CreateAttempts { get; private set; }
		public List<string> Shown { get; } = new();

		public IReadOnlyDictionary<uint, INativeNotification> ActiveNotifications => _active;

		public event EventHandler<NativeNotificationCompletedEventArgs>? NotificationCompleted;

		public INativeNotification? CreateNotification(string? category)
		{
			CreateAttempts++;
			CreatedOnUiThread = Dispatcher.UIThread.CheckAccess();

			return CreatedOnUiThread == true ? new FakeNotification(this) : null;
		}

		public void CloseAll() => _active.Clear();

		internal void Record(FakeNotification notification)
		{
			_active[notification.Id] = notification;
			Shown.Add(notification.Title ?? string.Empty);
		}

		internal void Raise(NativeNotificationCompletedEventArgs args) => NotificationCompleted?.Invoke(this, args);
	}

	private sealed class FakeNotification(ThreadAffineManager manager) : INativeNotification
	{
		public uint Id => 1;
		public string Category => string.Empty;
		public string? Title { get; set; }
		public string? Tag { get; set; }
		public string? Message { get; set; }
		public TimeSpan? Expiration { get; set; }
		public Bitmap? Icon { get; set; }
		public string? ReplyActionTag { get; set; }
		public IReadOnlyList<NativeNotificationAction> Actions { get; private set; } = [];

		public void SetActions(IReadOnlyList<NativeNotificationAction> actions) => Actions = actions;

		public void Show() => manager.Record(this);

		public void Close() { }
	}

	/// <summary>
	/// Runs <paramref name="body"/> on a thread of its own and waits for it, so that "off the UI
	/// thread" is a fact about the caller rather than about how the thread pool felt like scheduling.
	/// </summary>
	private static Task OffTheUiThreadAsync(Func<Task> body)
	{
		var finished = new TaskCompletionSource();

		var thread = new Thread(() =>
		{
			try
			{
				body().GetAwaiter().GetResult();
				finished.SetResult();
			}
			catch (Exception ex)
			{
				finished.SetException(ex);
			}
		})
		{
			IsBackground = true,
			Name = "off-ui-thread caller",
		};

		thread.Start();

		return finished.Task;
	}

	/// <summary>
	/// Registers the manager and runs <paramref name="body"/> inside one headless session dispatch.
	/// <para>
	/// The body has to run <em>inside</em> the dispatch, not merely after one. Dispatcher.UIThread
	/// identifies the session's thread only while a dispatch is in flight; outside one it binds to
	/// whichever thread asks it, so CheckAccess() answers True everywhere and a caller that genuinely
	/// did step off the UI thread is told that it did not. What used to decide this test was whether
	/// some other test in the assembly happened to have a dispatch in flight at that moment — it
	/// passed on a machine with cores to spare and went red on the two-core CI runner, and it fails
	/// every time if you run this class on its own.
	/// </para>
	/// </summary>
	private static Task WithManagerAsync(ThreadAffineManager manager, Func<Task> body) =>
		UiThread.Dispatch(async () =>
		{
			NativeNotificationManager.RegisterNativeNotificationManager(manager);
			try
			{
				await body();
			}
			finally
			{
				NativeNotificationManager.RegisterNativeNotificationManager(null!);
			}
		});

	/// <summary>
	/// The regression this guards: both callers are fire-and-forget and resume on the thread pool —
	/// the update check after its HTTP request, the Pomodoro service from a timer — and on Linux
	/// that meant every notification the application asked for was quietly discarded.
	/// </summary>
	[Fact]
	public async Task ShowNotification_FromTheThreadPool_StillReachesTheManagerOnTheUiThread()
	{
		var manager = new ThreadAffineManager();

		// A thread of its own: the body runs on the session's UI thread, so the "background" caller
		// has to be put somewhere that demonstrably is not it.
		await WithManagerAsync(manager, () => OffTheUiThreadAsync(async () =>
		{
			var service = new SystemNotificationService(NullLogger<SystemNotificationService>.Instance);

			Dispatcher.UIThread.CheckAccess().Should()
				.BeFalse("the test has to stand where the update check stands");

			await service.ShowNotificationAsync("WorkTracker", "New version available");
		}));

		manager.CreateAttempts.Should().Be(1);
		manager.CreatedOnUiThread.Should().BeTrue("the manager only creates notifications on the UI thread");
		manager.Shown.Should().Equal("WorkTracker");
	}

	[Fact]
	public async Task ShowNotification_AlreadyOnTheUiThread_DoesNotMarshalAgain()
	{
		var manager = new ThreadAffineManager();

		// No dispatch of its own: WithManagerAsync already runs this on the UI thread.
		await WithManagerAsync(manager, async () =>
		{
			var service = new SystemNotificationService(NullLogger<SystemNotificationService>.Instance);

			Dispatcher.UIThread.CheckAccess().Should()
				.BeTrue("this test has to stand where the Pomodoro timer's UI callback stands");

			await service.ShowNotificationAsync("WorkTracker", "Pomodoro finished");
		});

		manager.CreatedOnUiThread.Should().BeTrue();
		manager.Shown.Should().Equal("WorkTracker");
	}

	[Fact]
	public async Task ShowNotification_WithNoManagerRegistered_DoesNotThrow()
	{
		await UiThread.Dispatch(() => { });

		var service = new SystemNotificationService(NullLogger<SystemNotificationService>.Instance);

		var show = async () => await service.ShowNotificationAsync("WorkTracker", "anything");

		await show.Should().NotThrowAsync();
	}
}
