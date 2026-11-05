namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin interface for external controllers that read and drive the running application
/// (e.g. control surfaces like Stream Deck). Only an interactive frontend attaches these plugins:
/// the CLI loads them too, but never hands them a host.
/// </summary>
public interface IRemoteControlPlugin : IPlugin
{
	/// <summary>
	/// Connects the plugin to the application, until it gets shut down. Called each time the plugin
	/// configuration is saved as well, so it must be idempotent.
	/// </summary>
	/// <param name="host">Access to the application state and commands.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task AttachAsync(IRemoteControlHost host, CancellationToken cancellationToken);
}
