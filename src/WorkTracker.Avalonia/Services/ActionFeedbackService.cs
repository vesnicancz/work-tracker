using Microsoft.Extensions.Logging;

namespace WorkTracker.Avalonia.Services;

public sealed class ActionFeedbackService : IActionFeedbackService
{
	private const string NotificationTitle = "WorkTracker";

	private readonly INotificationService _notificationService;
	private readonly ISystemNotificationService _systemNotification;
	private readonly IUiDispatcher _uiDispatcher;
	private readonly ILogger<ActionFeedbackService> _logger;

	public ActionFeedbackService(
		INotificationService notificationService,
		ISystemNotificationService systemNotification,
		IUiDispatcher uiDispatcher,
		ILogger<ActionFeedbackService> logger)
	{
		_notificationService = notificationService;
		_systemNotification = systemNotification;
		_uiDispatcher = uiDispatcher;
		_logger = logger;
	}

	public Task ReportSuccessAsync(string message, CancellationToken cancellationToken)
	{
		return ReportAsync(message, _notificationService.ShowSuccess, cancellationToken);
	}

	public Task ReportFailureAsync(string message, CancellationToken cancellationToken)
	{
		return ReportAsync(message, _notificationService.ShowError, cancellationToken);
	}

	private async Task ReportAsync(string message, Action<string> showToast, CancellationToken cancellationToken)
	{
		try
		{
			await _uiDispatcher.InvokeAsync(async () =>
			{
				if (_notificationService.IsVisibleToUser)
				{
					showToast(message);
				}
				else
				{
					await _systemNotification.ShowReplacingNotificationAsync(NotificationTitle, message);
				}

				return true;
			}, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			// Shutting down: the dispatcher no longer runs what is posted to it
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to report the outcome of an action");
		}
	}
}
