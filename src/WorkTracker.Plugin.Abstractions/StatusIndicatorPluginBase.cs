using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for status indicator plugins. Inherits configuration, validation, and lifecycle from <see cref="PluginBase"/>.
/// </summary>
public abstract class StatusIndicatorPluginBase(ILogger logger) : PluginBase(logger), IStatusIndicatorPlugin
{
	public abstract bool IsDeviceAvailable { get; }

	public abstract Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken);
}
