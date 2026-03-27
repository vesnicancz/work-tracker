namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for status indicator plugins. Inherits configuration, validation, and lifecycle from <see cref="PluginBase"/>.
/// </summary>
public abstract class StatusIndicatorPluginBase : PluginBase, IStatusIndicatorPlugin
{
	public abstract bool IsDeviceAvailable { get; }

	public abstract Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken);
}
