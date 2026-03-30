namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Immutable snapshot of Pomodoro timer state. Avoids multiple lock acquisitions
/// when reading several properties at once.
/// </summary>
public sealed record PomodoroSnapshot(
	PomodoroPhase CurrentPhase,
	TimeSpan TimeRemaining,
	int CompletedPomodoros,
	int PomodorosBeforeLongBreak,
	bool IsRunning);
