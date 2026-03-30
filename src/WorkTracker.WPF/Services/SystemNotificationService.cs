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
	private volatile string? _pendingActionUrl;

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

			_pendingActionUrl = actionUrl;

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
		var url = Interlocked.Exchange(ref _pendingActionUrl, null);
		if (url != null && IsHttpUrl(url))
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

	private static bool IsHttpUrl(string url) =>
		Uri.TryCreate(url, UriKind.Absolute, out var uri)
		&& uri.Scheme is "https" or "http";

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
