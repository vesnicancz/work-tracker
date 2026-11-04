namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Keeps remote control plugins connected to the application according to their enabled state.
/// </summary>
public interface IRemoteControlCoordinator
{
	/// <summary>
	/// Attaches the enabled remote control plugins and detaches the disabled ones. Call after plugins
	/// were loaded and after the enabled state changed.
	/// </summary>
	Task SyncAsync(CancellationToken cancellationToken);
}
