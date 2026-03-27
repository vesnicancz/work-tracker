namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin interface for devices that visually indicate the current Pomodoro phase
/// (e.g. LED indicators, smart lights). The plugin receives state changes from the
/// Pomodoro timer and translates them into device-specific actions.
/// </summary>
public interface IStatusIndicatorPlugin : IPlugin
{
	/// <summary>
	/// Gets whether the physical device is currently connected and ready to receive commands.
	/// </summary>
	bool IsDeviceAvailable { get; }

	/// <summary>
	/// Updates the device to reflect the given Pomodoro phase.
	/// Called by the Pomodoro timer on every phase transition and on stop/reset.
	/// </summary>
	/// <param name="state">The current Pomodoro phase.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken);
}
