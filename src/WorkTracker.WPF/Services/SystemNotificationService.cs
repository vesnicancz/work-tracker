using System.Collections.Concurrent;
using System.Diagnostics;
using DesktopNotifications;
using DesktopNotifications.Windows;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.Services;

public sealed class SystemNotificationService : ISystemNotificationService, IDisposable
{
	private readonly ILogger<SystemNotificationService> _logger;
	private readonly WindowsNotificationManager _manager;
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private volatile bool _initialized;
	private readonly ConcurrentDictionary<string, string> _pendingActionUrls = new();

	public SystemNotificationService(ILogger<SystemNotificationService> logger)
	{
		_logger = logger;
		var context = WindowsApplicationContext.FromCurrentProcess("WorkTracker");
		_manager = new WindowsNotificationManager(context);
		_manager.NotificationActivated += OnNotificationActivated;
	}

	public Task ShowNotificationAsync(string title, string message) =>
		ShowNotificationAsync(title, message, null);

	public async Task ShowNotificationAsync(string title, string message, string? actionUrl)
	{
		try
		{
			await EnsureInitializedAsync();

			var notification = new Notification
			{
				Title = title,
				Body = message
			};

			if (!string.IsNullOrEmpty(actionUrl))
			{
				_pendingActionUrls[title] = actionUrl;
			}

			await _manager.ShowNotification(notification, null);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to show system notification");
		}
	}

	public void Dispose()
	{
		_manager.NotificationActivated -= OnNotificationActivated;
		_initLock.Dispose();
		_manager.Dispose();
	}

	private void OnNotificationActivated(object? sender, NotificationActivatedEventArgs e)
	{
		// DesktopNotifications doesn't provide a reliable notification ID in the activated event,
		// so we pop the first available URL (typically only one is pending at a time)
		var key = _pendingActionUrls.Keys.FirstOrDefault();
		if (key != null && _pendingActionUrls.TryRemove(key, out var url))
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

	private async Task EnsureInitializedAsync()
	{
		if (_initialized)
		{
			return;
		}

		await _initLock.WaitAsync();
		try
		{
			if (!_initialized)
			{
				await _manager.Initialize();
				_initialized = true;
			}
		}
		finally
		{
			_initLock.Release();
		}
	}
}
