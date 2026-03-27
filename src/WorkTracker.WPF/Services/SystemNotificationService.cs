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

	public SystemNotificationService(ILogger<SystemNotificationService> logger)
	{
		_logger = logger;
		var context = WindowsApplicationContext.FromCurrentProcess("WorkTracker");
		_manager = new WindowsNotificationManager(context);
	}

	public async Task ShowNotificationAsync(string title, string message)
	{
		try
		{
			await EnsureInitializedAsync();

			await _manager.ShowNotification(new Notification
			{
				Title = title,
				Body = message
			}, null);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to show system notification");
		}
	}

	public void Dispose()
	{
		_initLock.Dispose();
		_manager.Dispose();
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
