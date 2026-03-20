namespace WorkTracker.WPF.Services;

/// <summary>
/// Service for managing global hotkeys
/// </summary>
public interface IHotkeyService : IDisposable
{
	/// <summary>
	/// Registers the global hotkey for new work entry
	/// </summary>
	void Register();

	/// <summary>
	/// Unregisters the global hotkey
	/// </summary>
	void Unregister();

	/// <summary>
	/// Event raised when the hotkey is pressed
	/// </summary>
	event EventHandler? HotkeyPressed;
}
