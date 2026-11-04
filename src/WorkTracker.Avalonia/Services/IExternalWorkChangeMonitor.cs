namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Brings the application up to date when work entries change outside of it, typically through the CLI.
/// </summary>
public interface IExternalWorkChangeMonitor
{
	/// <summary>
	/// Starts watching. Call after <see cref="IWorklogStateService.InitializeAsync"/>.
	/// </summary>
	void Start();
}
