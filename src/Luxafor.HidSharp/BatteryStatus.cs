namespace Luxafor.HidSharp;

/// <summary>
/// Battery charging status of the Luxafor device.
/// </summary>
public enum BatteryStatus : byte
{
	/// <summary>Device is not plugged in / not charging.</summary>
	NotConnected = 0,

	/// <summary>Device is currently charging.</summary>
	Charging = 1,

	/// <summary>Battery is fully charged.</summary>
	Full = 2
}
