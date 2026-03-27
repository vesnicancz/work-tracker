namespace WorkTracker.UI.Shared.Models;

public class PomodoroSettings
{
	public bool Enabled { get; set; }
	public int WorkMinutes { get; set; } = 25;
	public int ShortBreakMinutes { get; set; } = 5;
	public int LongBreakMinutes { get; set; } = 15;
	public int PomodorosBeforeLongBreak { get; set; } = 4;
	public bool AutoStartWorkTracking { get; set; }
	public bool AutoStopWorkTracking { get; set; }
	public bool LuxaforEnabled { get; set; }
}
