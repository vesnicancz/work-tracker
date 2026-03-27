namespace Luxafor.HidSharp;

/// <summary>
/// Specifies which LED(s) to target on the Luxafor device.
/// </summary>
public enum Led : byte
{
	/// <summary>All LEDs (both sides).</summary>
	All = 0xFF,

	/// <summary>Top side LEDs (1-3).</summary>
	TopSide = 0x41,

	/// <summary>Bottom side LEDs (4-6).</summary>
	BottomSide = 0x42,

	/// <summary>LED 1 (top side).</summary>
	Led1 = 0x01,

	/// <summary>LED 2 (top side).</summary>
	Led2 = 0x02,

	/// <summary>LED 3 (top side).</summary>
	Led3 = 0x03,

	/// <summary>LED 4 (bottom side).</summary>
	Led4 = 0x04,

	/// <summary>LED 5 (bottom side).</summary>
	Led5 = 0x05,

	/// <summary>LED 6 (bottom side).</summary>
	Led6 = 0x06
}
