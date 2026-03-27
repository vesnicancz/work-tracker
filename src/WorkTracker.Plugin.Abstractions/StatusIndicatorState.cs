namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Represents the current Pomodoro phase for status indicator plugins.
/// </summary>
public enum StatusIndicatorState
{
	/// <summary>Timer is not running. Device should turn off or show neutral state.</summary>
	Idle,

	/// <summary>Work phase is active.</summary>
	Work,

	/// <summary>Short break between work sessions.</summary>
	ShortBreak,

	/// <summary>Long break after completing a full Pomodoro cycle.</summary>
	LongBreak
}
