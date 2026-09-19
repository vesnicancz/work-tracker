using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Labs.Notifications;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public sealed class SystemNotificationService : ISystemNotificationService, IDisposable
{
	private readonly ILogger<SystemNotificationService> _logger;
	private readonly ConcurrentDictionary<uint, string> _pendingActionUrls = new();
	private INativeNotificationManager? _manager;

	public SystemNotificationService(ILogger<SystemNotificationService> logger)
	{
		_logger = logger;

		_manager = NativeNotificationManager.Current;
		if (_manager != null)
		{
			_manager.NotificationCompleted += OnNotificationCompleted;
		}
	}

	public Task ShowNotificationAsync(string title, string message) =>
		ShowNotificationAsync(title, message, null);

	/// <summary>
	/// Shows a notification, moving to the UI thread first when it has to.
	/// <para>
	/// The Linux notification manager is thread-affine: off the UI thread its CreateNotification
	/// hands back null rather than throwing, and nothing at all reaches the notification portal.
	/// Both callers are fire-and-forget — the update check resumes on the thread pool after its
	/// HTTP request, the Pomodoro service posts from a timer — so on Linux no notification this
	/// application asked for was ever delivered. Windows tolerates the background thread, which is
	/// why the same code appeared to work there.
	/// </para>
	/// </summary>
	public async Task ShowNotificationAsync(string title, string message, string? actionUrl)
	{
		try
		{
			if (Dispatcher.UIThread.CheckAccess())
			{
				Show(title, message, actionUrl);
				return;
			}

			await Dispatcher.UIThread.InvokeAsync(() => Show(title, message, actionUrl));
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to show system notification");
		}
	}

	/// <summary>
	/// Hands the notification to the platform. Runs on the UI thread. A notification that cannot be
	/// created is logged rather than dropped in silence: both of these used to be a bare return,
	/// which is why a notification that never appeared left nothing behind to explain it.
	/// </summary>
	private void Show(string title, string message, string? actionUrl)
	{
		var manager = NativeNotificationManager.Current;
		if (manager == null)
		{
			_logger.LogWarning(
				"No native notification manager is registered; dropping the notification {Title}", title);
			return;
		}

		var notification = manager.CreateNotification(null);
		if (notification == null)
		{
			_logger.LogWarning(
				"{Manager} created no notification; dropping {Title}", manager.GetType().Name, title);
			return;
		}

		notification.Title = title;
		notification.Message = message;

		if (!string.IsNullOrEmpty(actionUrl))
		{
			_pendingActionUrls[notification.Id] = actionUrl;
		}

		notification.Show();
	}

	public void Dispose()
	{
		if (_manager != null)
		{
			_manager.NotificationCompleted -= OnNotificationCompleted;
			_manager = null;
		}
	}

	private void OnNotificationCompleted(object? sender, NativeNotificationCompletedEventArgs e)
	{
		if (e.NotificationId == null)
		{
			return;
		}

		if (e.IsActivated && _pendingActionUrls.TryRemove(e.NotificationId.Value, out var url))
		{
			if (IsHttpUrl(url))
			{
				try
				{
					Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to open URL {Url}", url);
				}
			}
		}
		else
		{
			// Clean up dismissed/expired notifications
			_pendingActionUrls.TryRemove(e.NotificationId.Value, out _);
		}
	}

	private static bool IsHttpUrl(string url) =>
		Uri.TryCreate(url, UriKind.Absolute, out var uri)
		&& uri.Scheme is "https" or "http";
}
