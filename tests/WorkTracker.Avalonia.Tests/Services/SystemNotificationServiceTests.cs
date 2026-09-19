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

	private static async Task WithManagerAsync(ThreadAffineManager manager, Func<Task> body)
	{
		// Touch the headless session first: Dispatcher.UIThread binds to whichever thread reaches it
		// first, and that has to be the session's, not the test's, or standing "off the UI thread"
		// below would be a lie.
		await UiThread.Dispatch(() => { });

		NativeNotificationManager.RegisterNativeNotificationManager(manager);
		try
		{
			await body();
		}
		finally
		{
			NativeNotificationManager.RegisterNativeNotificationManager(null!);
		}
	}

	/// <summary>
	/// The regression this guards: both callers are fire-and-forget and resume on the thread pool —
	/// the update check after its HTTP request, the Pomodoro service from a timer — and on Linux
	/// that meant every notification the application asked for was quietly discarded.
	/// </summary>
	[Fact]
	public async Task ShowNotification_FromTheThreadPool_StillReachesTheManagerOnTheUiThread()
	{
		var manager = new ThreadAffineManager();

		// Task.Run, not a bare await: awaiting the headless session hands the continuation back to
		// its dispatcher, so without this the "background" caller would already be on the UI thread.
		await WithManagerAsync(manager, () => Task.Run(async () =>
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

		await WithManagerAsync(manager, () => UiThread.Dispatch(async () =>
		{
			var service = new SystemNotificationService(NullLogger<SystemNotificationService>.Instance);
			await service.ShowNotificationAsync("WorkTracker", "Pomodoro finished");
		}));

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
