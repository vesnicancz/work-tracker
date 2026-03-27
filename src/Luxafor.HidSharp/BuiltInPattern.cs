namespace Luxafor.HidSharp;

/// <summary>
/// Built-in hardware animation patterns available on the Luxafor device.
/// These are played entirely on-device via the pattern command.
/// </summary>
public enum BuiltInPattern : byte
{
	/// <summary>Red/green traffic light alternation.</summary>
	TrafficLights = 1,

	/// <summary>Random color pattern 1.</summary>
	Random1 = 2,

	/// <summary>Random color pattern 2.</summary>
	Random2 = 3,

	/// <summary>Random color pattern 3.</summary>
	Random3 = 4,

	/// <summary>Blue/red police siren pattern.</summary>
	Police = 5,

	/// <summary>Random color pattern 4.</summary>
	Random4 = 6,

	/// <summary>Random color pattern 5.</summary>
	Random5 = 7,

	/// <summary>Rainbow color cycle.</summary>
	Rainbow = 8
}
