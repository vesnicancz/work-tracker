namespace Luxafor.HidSharp;

/// <summary>
/// Wave animation types for the Luxafor device.
/// </summary>
public enum WaveType : byte
{
	/// <summary>Short wave.</summary>
	Short = 1,

	/// <summary>Long wave.</summary>
	Long = 2,

	/// <summary>Short overlapping wave.</summary>
	ShortOverlapping = 3,

	/// <summary>Long overlapping wave.</summary>
	LongOverlapping = 4,

	/// <summary>Smooth wave.</summary>
	Smooth = 5
}
