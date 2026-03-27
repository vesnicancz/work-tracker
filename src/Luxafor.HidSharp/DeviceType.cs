namespace Luxafor.HidSharp;

/// <summary>
/// Luxafor device type, detected from the device identification report.
/// </summary>
public enum DeviceType
{
	/// <summary>Standard Luxafor Flag (USB only).</summary>
	Standard,

	/// <summary>Luxafor Bluetooth Pro (with USB dongle).</summary>
	Bluetooth,

	/// <summary>Luxafor Mute Button.</summary>
	MuteButton,

	/// <summary>Luxafor Smart Button.</summary>
	SmartButton,

	/// <summary>Luxafor Colorblind device.</summary>
	Colorblind
}
