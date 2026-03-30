namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for showing OS-level system notifications (toast / notification center).
/// Used for important events like Pomodoro phase transitions that need to be visible
/// even when the application window is minimized or in the system tray.
/// </summary>
public interface ISystemNotificationService
{
	Task ShowNotificationAsync(string title, string message);

	/// <summary>
	/// Shows a system notification with an optional action URL that opens in the browser when clicked.
	/// </summary>
	Task ShowNotificationAsync(string title, string message, string? actionUrl);
}
