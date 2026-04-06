namespace WorkTracker.UI.Shared.Services;

public interface IPomodoroService : IAsyncDisposable
{
	PomodoroPhase CurrentPhase { get; }
	TimeSpan TimeRemaining { get; }
	int CompletedPomodoros { get; }
	int PomodorosBeforeLongBreak { get; }
	bool IsRunning { get; }

	/// <summary>
	/// Returns an atomic snapshot of all timer state in a single lock acquisition.
	/// Prefer this over reading individual properties when multiple values are needed.
	/// </summary>
	PomodoroSnapshot GetSnapshot();

	void Start();

	void Stop();

	void Skip();

	void Reset();

	event EventHandler<PomodoroPhase>? PhaseChanged;

	event EventHandler? Tick;

	event EventHandler? PomodoroCompleted;
}
