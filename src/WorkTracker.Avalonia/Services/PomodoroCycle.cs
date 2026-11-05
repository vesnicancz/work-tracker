using WorkTracker.Avalonia.Models;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// The rules of the Pomodoro: which phase follows which, how long it lasts, and when the long break comes.
/// It knows nothing of clocks, devices or threads. <see cref="PomodoroService"/> feeds it the passing time
/// under its lock and acts on what comes back.
/// </summary>
public sealed class PomodoroCycle
{
	public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;

	public TimeSpan TimeRemaining { get; private set; }

	public int CompletedPomodoros { get; private set; }

	/// <summary>
	/// What the running Pomodoro started with. Changing the settings doesn't affect it until the next start.
	/// </summary>
	public PomodoroSettings Settings { get; private set; } = new();

	public bool IsRunning => Phase != PomodoroPhase.Idle;

	/// <returns>False when a Pomodoro is already running, which then carries on untouched.</returns>
	public bool Start(PomodoroSettings settings)
	{
		if (IsRunning)
		{
			return false;
		}

		Settings = settings;
		CompletedPomodoros = 0;
		EnterPhase(PomodoroPhase.Work, settings.WorkMinutes);
		return true;
	}

	/// <summary>
	/// Ends the running Pomodoro. The count of completed ones stays to be seen until the next start.
	/// </summary>
	/// <returns>False when nothing was running.</returns>
	public bool Stop()
	{
		if (!IsRunning)
		{
			return false;
		}

		EnterIdle();
		return true;
	}

	public void Reset()
	{
		EnterIdle();
		CompletedPomodoros = 0;
	}

	/// <returns>Whether the phase has run out, and it is time to <see cref="Advance"/>.</returns>
	public bool Tick(TimeSpan elapsed)
	{
		if (!IsRunning)
		{
			return false;
		}

		TimeRemaining -= elapsed;
		return TimeRemaining <= TimeSpan.Zero;
	}

	/// <returns>What changed, or null when nothing was running.</returns>
	public PomodoroTransition? Advance()
	{
		var from = Phase;

		switch (Phase)
		{
			case PomodoroPhase.Work:
				CompletedPomodoros++;
				if (CompletedPomodoros >= Settings.PomodorosBeforeLongBreak)
				{
					EnterPhase(PomodoroPhase.LongBreak, Settings.LongBreakMinutes);
				}
				else
				{
					EnterPhase(PomodoroPhase.ShortBreak, Settings.ShortBreakMinutes);
				}

				return new PomodoroTransition(from, Phase, CompletedPomodoro: true);

			case PomodoroPhase.ShortBreak:
				EnterPhase(PomodoroPhase.Work, Settings.WorkMinutes);
				return new PomodoroTransition(from, Phase, CompletedPomodoro: false);

			case PomodoroPhase.LongBreak:
				CompletedPomodoros = 0;
				EnterPhase(PomodoroPhase.Work, Settings.WorkMinutes);
				return new PomodoroTransition(from, Phase, CompletedPomodoro: false);

			default:
				return null;
		}
	}

	private void EnterPhase(PomodoroPhase phase, int minutes)
	{
		Phase = phase;
		TimeRemaining = TimeSpan.FromMinutes(minutes);
	}

	private void EnterIdle()
	{
		Phase = PomodoroPhase.Idle;
		TimeRemaining = TimeSpan.Zero;
	}
}

/// <param name="From">The phase that ended.</param>
/// <param name="To">The phase that began.</param>
/// <param name="CompletedPomodoro">Whether a work phase has just been finished.</param>
public readonly record struct PomodoroTransition(PomodoroPhase From, PomodoroPhase To, bool CompletedPomodoro);
