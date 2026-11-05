using Avalonia.Controls;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// The window the application lifetime treats as the main one, and what the rest of the application
/// asks of it. Everything is to be called on the UI thread.
/// </summary>
public interface IMainWindowAccessor
{
	/// <summary>
	/// Null while an application started minimized has not finished its bootstrap.
	/// </summary>
	Window? MainWindow { get; }

	void BringForward();

	void RequestShutdown();
}
