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
		var enabled = pluginManager.RemoteControlPlugins.ToHashSet();

		foreach (var plugin in pluginManager.AllRemoteControlPlugins)
		{
			try
			{
				if (enabled.Contains(plugin))
				{
					await plugin.AttachAsync(host, cancellationToken);
				}
				else
				{
					await plugin.DetachAsync();
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// One broken plugin must not keep the others from connecting, or fail saving the settings
				logger.LogError(ex, "Failed to update remote control plugin {Name}", plugin.Metadata.Name);
			}
		}
	}
}
