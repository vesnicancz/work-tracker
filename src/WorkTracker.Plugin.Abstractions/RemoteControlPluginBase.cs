using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for remote control plugins. Inherits configuration, validation, and lifecycle from <see cref="PluginBase"/>.
/// </summary>
public abstract class RemoteControlPluginBase(ILogger logger) : PluginBase(logger), IRemoteControlPlugin
{
	public abstract Task AttachAsync(IRemoteControlHost host, CancellationToken cancellationToken);
}
