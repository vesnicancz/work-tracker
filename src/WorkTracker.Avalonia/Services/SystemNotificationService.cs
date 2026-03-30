using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Labs.Notifications;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public sealed class SystemNotificationService : ISystemNotificationService
{
	private readonly ILogger<SystemNotificationService> _logger;
	private readonly ConcurrentDictionary<uint, string> _pendingActionUrls = new();

	public SystemNotificationService(ILogger<SystemNotificationService> logger)
	{
		_logger = logger;

		var manager = NativeNotificationManager.Current;
		if (manager != null)
		{
			manager.NotificationCompleted += OnNotificationCompleted;
		}
	}

	public Task ShowNotificationAsync(string title, string message) =>
		ShowNotificationAsync(title, message, null);

	public Task ShowNotificationAsync(string title, string message, string? actionUrl)
	{
		try
		{
			var manager = NativeNotificationManager.Current;
			if (manager == null)
			{
				return Task.CompletedTask;
			}

			var notification = manager.CreateNotification(null);
			if (notification == null)
			{
				return Task.CompletedTask;
			}

			notification.Title = title;
			notification.Message = message;

			if (!string.IsNullOrEmpty(actionUrl))
			{
				_pendingActionUrls[notification.Id] = actionUrl;
			}

			notification.Show();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to show system notification");
		}

		return Task.CompletedTask;
	}

	private void OnNotificationCompleted(object? sender, NativeNotificationCompletedEventArgs e)
	{
		if (e.NotificationId == null)
		{
			return;
		}

		if (e.IsActivated && _pendingActionUrls.TryRemove(e.NotificationId.Value, out var url))
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
		else
		{
			// Clean up dismissed/expired notifications
			_pendingActionUrls.TryRemove(e.NotificationId.Value, out _);
		}
	}
}
