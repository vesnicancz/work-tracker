namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for showing toast notifications
/// </summary>
public interface INotificationService
{
	/// <summary>
	/// Shows a success notification
	/// </summary>
	/// <param name="message">Message to display</param>
	void ShowSuccess(string message);

	/// <summary>
	/// Shows an information notification
	/// </summary>
	/// <param name="message">Message to display</param>
	void ShowInformation(string message);

	/// <summary>
	/// Shows a warning notification
	/// </summary>
	/// <param name="message">Message to display</param>
	void ShowWarning(string message);

	/// <summary>
	/// Shows an error notification
	/// </summary>
	/// <param name="message">Message to display</param>
	void ShowError(string message);
}
