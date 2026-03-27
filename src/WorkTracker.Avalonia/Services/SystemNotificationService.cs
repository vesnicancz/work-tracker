using Avalonia.Labs.Notifications;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public sealed class SystemNotificationService : ISystemNotificationService
{
	private readonly ILogger<SystemNotificationService> _logger;

	public SystemNotificationService(ILogger<SystemNotificationService> logger)
	{
		_logger = logger;
	}

	public Task ShowNotificationAsync(string title, string message)
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
			notification.Show();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to show system notification");
		}

		return Task.CompletedTask;
	}
}
