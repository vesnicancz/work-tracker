namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Button set shown by <see cref="MessageBoxWindow"/>.
/// </summary>
public enum MessageBoxButtons
{
	/// <summary>Single acknowledging button.</summary>
	Ok,

	/// <summary>Yes/No confirmation — <see cref="MessageBoxWindow.Result"/> is <c>true</c> for Yes.</summary>
	YesNo,

	/// <summary>Retry or close the application — <see cref="MessageBoxWindow.Result"/> is <c>true</c> for Retry.</summary>
	RetryClose,
}
