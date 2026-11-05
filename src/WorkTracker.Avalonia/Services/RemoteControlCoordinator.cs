using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Services;

public sealed class RemoteControlCoordinator(
	IPluginManager pluginManager,
	IRemoteControlHost host,
	ILogger<RemoteControlCoordinator> logger) : IRemoteControlCoordinator
{
	public async Task SyncAsync(CancellationToken cancellationToken)
	{
		foreach (var plugin in pluginManager.RemoteControlPlugins)
		{
			try
			{
				await plugin.AttachAsync(host, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// One broken plugin must not keep the others from connecting, or fail saving the settings
				logger.LogError(ex, "Failed to attach remote control plugin {Name}", plugin.Metadata.Name);
			}
		}
	}
}
