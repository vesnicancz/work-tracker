namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin interface for external controllers that read and drive the running application
/// (e.g. control surfaces like Stream Deck). Only an interactive frontend attaches these plugins:
/// the CLI loads them too, but never hands them a host.
/// </summary>
public interface IRemoteControlPlugin : IPlugin
{
	/// <summary>
	/// Connects the plugin to the application. Called when the plugin is enabled, and again each time
	/// the plugin configuration is saved, so it must be idempotent.
	/// </summary>
	/// <param name="host">Access to the application state and commands.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task AttachAsync(IRemoteControlHost host, CancellationToken cancellationToken);

	/// <summary>
	/// Disconnects the plugin when the user disables it. The plugin stays loaded and can be attached
	/// again. Must be idempotent.
	/// </summary>
	Task DetachAsync();
}
