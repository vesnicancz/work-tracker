using FluentAssertions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class PomodoroCycleTests
{
	private static readonly PomodoroSettings Settings = new()
	{
		WorkMinutes = 25,
		ShortBreakMinutes = 5,
		LongBreakMinutes = 15,
		PomodorosBeforeLongBreak = 2
	};

	private readonly PomodoroCycle _cycle = new();

	[Fact]
	public void New_IsIdle()
	{
		_cycle.Phase.Should().Be(PomodoroPhase.Idle);
		_cycle.IsRunning.Should().BeFalse();
		_cycle.TimeRemaining.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void Start_BeginsTheWorkPhase()
	{
		_cycle.Start(Settings).Should().BeTrue();

		_cycle.Phase.Should().Be(PomodoroPhase.Work);
		_cycle.IsRunning.Should().BeTrue();
		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(25));
	}

	[Fact]
	public void Start_WhileRunning_LeavesItAlone()
	{
		_cycle.Start(Settings);
		_cycle.Tick(TimeSpan.FromMinutes(1));
		var otherSettings = new PomodoroSettings { WorkMinutes = 50 };

		_cycle.Start(otherSettings).Should().BeFalse();

		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(24));
		_cycle.Settings.Should().BeSameAs(Settings);
	}

	[Fact]
	public void Start_AfterAStop_CountsFromZero()
	{
		_cycle.Start(Settings);
		_cycle.Advance();
		_cycle.Stop();

		_cycle.Start(Settings);

		_cycle.CompletedPomodoros.Should().Be(0);
	}

	[Fact]
	public void Tick_BeforeTheEnd_OnlyCountsDown()
	{
		_cycle.Start(Settings);

		_cycle.Tick(TimeSpan.FromSeconds(1)).Should().BeFalse();

		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(1));
		_cycle.Phase.Should().Be(PomodoroPhase.Work);
	}

	[Fact]
	public void Tick_AtTheEnd_ReportsItAndWaitsForAdvance()
	{
		_cycle.Start(Settings);

		_cycle.Tick(TimeSpan.FromMinutes(25)).Should().BeTrue();

		_cycle.Phase.Should().Be(PomodoroPhase.Work);
	}

	[Fact]
	public void Tick_WhenIdle_DoesNothing()
	{
		_cycle.Tick(TimeSpan.FromMinutes(1)).Should().BeFalse();

		_cycle.TimeRemaining.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void Advance_GoesThroughTheWholeCycleAndStartsOver()
	{
		_cycle.Start(Settings);

		_cycle.Advance().Should().Be(new PomodoroTransition(PomodoroPhase.Work, PomodoroPhase.ShortBreak, true));
		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(5));
		_cycle.CompletedPomodoros.Should().Be(1);

		_cycle.Advance().Should().Be(new PomodoroTransition(PomodoroPhase.ShortBreak, PomodoroPhase.Work, false));
		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(25));
		_cycle.CompletedPomodoros.Should().Be(1);

		_cycle.Advance().Should().Be(new PomodoroTransition(PomodoroPhase.Work, PomodoroPhase.LongBreak, true));
		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(15));
		_cycle.CompletedPomodoros.Should().Be(2);

		_cycle.Advance().Should().Be(new PomodoroTransition(PomodoroPhase.LongBreak, PomodoroPhase.Work, false));
		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(25));
		_cycle.CompletedPomodoros.Should().Be(0);
	}

	[Fact]
	public void Advance_WhenIdle_ReportsNothing()
	{
		_cycle.Advance().Should().BeNull();

		_cycle.Phase.Should().Be(PomodoroPhase.Idle);
	}

	[Fact]
	public void Advance_UsesTheSettingsItStartedWith()
	{
		var settings = new PomodoroSettings { WorkMinutes = 25, ShortBreakMinutes = 5, PomodorosBeforeLongBreak = 4 };
		_cycle.Start(settings);

		_cycle.Advance();

		_cycle.Settings.Should().BeSameAs(settings);
		_cycle.TimeRemaining.Should().Be(TimeSpan.FromMinutes(5));
	}

	[Fact]
	public void Stop_EndsThePhaseAndKeepsTheCount()
	{
		_cycle.Start(Settings);
		_cycle.Advance();

		_cycle.Stop().Should().BeTrue();

		_cycle.Phase.Should().Be(PomodoroPhase.Idle);
		_cycle.TimeRemaining.Should().Be(TimeSpan.Zero);
		_cycle.CompletedPomodoros.Should().Be(1);
	}

	[Fact]
	public void Stop_WhenIdle_ReportsNothingToStop()
	{
		_cycle.Stop().Should().BeFalse();
	}

	[Fact]
	public void Reset_EndsThePhaseAndClearsTheCount()
	{
		_cycle.Start(Settings);
		_cycle.Advance();

		_cycle.Reset();

		_cycle.Phase.Should().Be(PomodoroPhase.Idle);
		_cycle.TimeRemaining.Should().Be(TimeSpan.Zero);
		_cycle.CompletedPomodoros.Should().Be(0);
	}
}
