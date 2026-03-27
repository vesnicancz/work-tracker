namespace WorkTracker.UI.Shared.Services;

public enum PomodoroPhase
{
	Idle,
	Work,
	ShortBreak,
	LongBreak
}

public interface IPomodoroService
{
	PomodoroPhase CurrentPhase { get; }
	TimeSpan TimeRemaining { get; }
	int CompletedPomodoros { get; }
	int PomodorosBeforeLongBreak { get; }
	bool IsRunning { get; }

	void Start();
	void Stop();
	void Skip();
	void Reset();

	event EventHandler<PomodoroPhase>? PhaseChanged;
	event EventHandler? Tick;
	event EventHandler? PomodoroCompleted;
}
