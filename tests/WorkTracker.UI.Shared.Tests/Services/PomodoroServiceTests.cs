using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

public class PomodoroServiceTests : IDisposable
{
	private readonly Mock<ISettingsService> _settingsService = new();
	private readonly Mock<INotificationService> _notificationService = new();
	private readonly Mock<IWorklogStateService> _worklogStateService = new();
	private readonly Mock<IPluginManager> _pluginManager = new();
	private readonly Mock<IStatusIndicatorPlugin> _statusIndicatorPlugin = new();
	private readonly Mock<ISystemNotificationService> _systemNotification = new();
	private readonly Mock<ILocalizationService> _localization = new();
	private readonly PomodoroService _sut;

	public PomodoroServiceTests()
	{
		var settings = new ApplicationSettings
		{
			Pomodoro = new PomodoroSettings
			{
				WorkMinutes = 1, // Use 1 min for faster tests
				ShortBreakMinutes = 1,
				LongBreakMinutes = 1,
				PomodorosBeforeLongBreak = 2
			}
		};
		_settingsService.Setup(s => s.Settings).Returns(settings);
		_worklogStateService.Setup(s => s.IsInitialized).Returns(true);
		_worklogStateService.Setup(s => s.IsTracking).Returns(false);
		_localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_pluginManager.Setup(p => p.StatusIndicatorPlugins).Returns(new List<IStatusIndicatorPlugin> { _statusIndicatorPlugin.Object });
		_statusIndicatorPlugin.Setup(p => p.SetStateAsync(It.IsAny<StatusIndicatorState>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

		_sut = new PomodoroService(
			_settingsService.Object,
			_notificationService.Object,
			_systemNotification.Object,
			_worklogStateService.Object,
			_pluginManager.Object,
			_localization.Object,
			TimeProvider.System,
			NullLogger<PomodoroService>.Instance);
	}

	public void Dispose()
	{
		_sut.Dispose();
	}

	[Fact]
	public void Start_SetsPhaseToWork()
	{
		_sut.Start();

		_sut.CurrentPhase.Should().Be(PomodoroPhase.Work);
		_sut.IsRunning.Should().BeTrue();
		_sut.CompletedPomodoros.Should().Be(0);
	}

	[Fact]
	public void Start_RaisesPhaseChangedEvent()
	{
		PomodoroPhase? receivedPhase = null;
		_sut.PhaseChanged += (_, phase) => receivedPhase = phase;

		_sut.Start();

		receivedPhase.Should().Be(PomodoroPhase.Work);
	}

	[Fact]
	public void Start_WhenAlreadyRunning_DoesNothing()
	{
		_sut.Start();
		var phaseChangeCount = 0;
		_sut.PhaseChanged += (_, _) => phaseChangeCount++;

		_sut.Start();

		phaseChangeCount.Should().Be(0);
	}

	[Fact]
	public void Stop_SetsPhaseToIdle()
	{
		_sut.Start();

		_sut.Stop();

		_sut.CurrentPhase.Should().Be(PomodoroPhase.Idle);
		_sut.IsRunning.Should().BeFalse();
	}

	[Fact]
	public void Stop_WhenNotRunning_DoesNothing()
	{
		var phaseChangeCount = 0;
		_sut.PhaseChanged += (_, _) => phaseChangeCount++;

		_sut.Stop();

		phaseChangeCount.Should().Be(0);
	}

	[Fact]
	public void Reset_ClearsCompletedPomodoros()
	{
		_sut.Start();

		_sut.Reset();

		_sut.CompletedPomodoros.Should().Be(0);
		_sut.IsRunning.Should().BeFalse();
		_sut.CurrentPhase.Should().Be(PomodoroPhase.Idle);
	}

	[Fact]
	public void Skip_WhenNotRunning_DoesNothing()
	{
		var phaseChangeCount = 0;
		_sut.PhaseChanged += (_, _) => phaseChangeCount++;

		_sut.Skip();

		phaseChangeCount.Should().Be(0);
	}

	[Fact]
	public void Skip_FromWork_TransitionsToShortBreak()
	{
		_sut.Start();
		PomodoroPhase? lastPhase = null;
		_sut.PhaseChanged += (_, phase) => lastPhase = phase;

		_sut.Skip();

		lastPhase.Should().Be(PomodoroPhase.ShortBreak);
		_sut.CompletedPomodoros.Should().Be(1);
	}

	[Fact]
	public void Skip_FromShortBreak_TransitionsToWork()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak

		PomodoroPhase? lastPhase = null;
		_sut.PhaseChanged += (_, phase) => lastPhase = phase;

		_sut.Skip(); // ShortBreak -> Work

		lastPhase.Should().Be(PomodoroPhase.Work);
	}

	[Fact]
	public void Skip_AfterEnoughPomodoros_TransitionsToLongBreak()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak (completed: 1)
		_sut.Skip(); // ShortBreak -> Work
		PomodoroPhase? lastPhase = null;
		_sut.PhaseChanged += (_, phase) => lastPhase = phase;

		_sut.Skip(); // Work -> LongBreak (completed: 2, threshold is 2)

		lastPhase.Should().Be(PomodoroPhase.LongBreak);
		_sut.CompletedPomodoros.Should().Be(2);
	}

	[Fact]
	public void Skip_FromLongBreak_TransitionsToWorkAndResetsCount()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak
		_sut.Skip(); // ShortBreak -> Work
		_sut.Skip(); // Work -> LongBreak

		PomodoroPhase? lastPhase = null;
		_sut.PhaseChanged += (_, phase) => lastPhase = phase;

		_sut.Skip(); // LongBreak -> Work

		lastPhase.Should().Be(PomodoroPhase.Work);
		_sut.CompletedPomodoros.Should().Be(0);
	}

	[Fact]
	public void Skip_FromWork_FiresPomodoroCompletedEvent()
	{
		_sut.Start();
		var pomodoroCompleted = false;
		_sut.PomodoroCompleted += (_, _) => pomodoroCompleted = true;

		_sut.Skip();

		pomodoroCompleted.Should().BeTrue();
	}

	[Fact]
	public void Skip_FromWork_ShowsCompletionNotification()
	{
		_sut.Start();

		_sut.Skip();

		_notificationService.Verify(n => n.ShowSuccess("PomodoroCompleted"), Times.Once);
	}

	[Fact]
	public void Skip_FromBreakToWork_ShowsBreakOverNotification()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak

		_sut.Skip(); // ShortBreak -> Work

		_notificationService.Verify(n => n.ShowInformation("PomodoroBreakOver"), Times.Once);
	}

	[Fact]
	public void Start_SetsStatusIndicatorToWork()
	{
		_sut.Start();

		_statusIndicatorPlugin.Verify(p => p.SetStateAsync(StatusIndicatorState.Work, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void Skip_ToShortBreak_SetsStatusIndicatorToShortBreak()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak

		_statusIndicatorPlugin.Verify(p => p.SetStateAsync(StatusIndicatorState.ShortBreak, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void Stop_SetsStatusIndicatorToIdle()
	{
		_sut.Start();
		_sut.Stop();

		_statusIndicatorPlugin.Verify(p => p.SetStateAsync(StatusIndicatorState.Idle, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void Start_WithNoPlugins_DoesNotThrow()
	{
		_pluginManager.Setup(p => p.StatusIndicatorPlugins).Returns(new List<IStatusIndicatorPlugin>());

		var act = () => _sut.Start();

		act.Should().NotThrow();
	}

	[Fact]
	public void Start_ReadsSettingsAtStartTime()
	{
		_sut.Start();

		_sut.PomodorosBeforeLongBreak.Should().Be(2);
		_sut.TimeRemaining.Should().Be(TimeSpan.FromMinutes(1));
	}

	[Fact]
	public void Skip_FromWork_ShowsSystemNotification()
	{
		_sut.Start();

		_sut.Skip(); // Work -> ShortBreak

		_systemNotification.Verify(
			n => n.ShowNotificationAsync("Pomodoro", "PomodoroBreakStarted"),
			Times.Once);
	}

	[Fact]
	public void Skip_FromBreakToWork_ShowsSystemNotification()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak

		_sut.Skip(); // ShortBreak -> Work

		_systemNotification.Verify(
			n => n.ShowNotificationAsync("Pomodoro", "PomodoroWorkPhaseStarted"),
			Times.Once);
	}

	[Fact]
	public void Skip_ToLongBreak_ShowsSystemNotification()
	{
		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak (completed: 1)
		_sut.Skip(); // ShortBreak -> Work

		_sut.Skip(); // Work -> LongBreak (completed: 2)

		_systemNotification.Verify(
			n => n.ShowNotificationAsync("Pomodoro", "PomodoroLongBreakStarted"),
			Times.Once);
	}

	[Fact]
	public void Skip_FromWorkToBreak_WithAutoStopEnabled_StopsTracking()
	{
		var settings = _settingsService.Object.Settings;
		settings.Pomodoro.AutoStopWorkTracking = true;
		_worklogStateService.Setup(s => s.IsTracking).Returns(true);

		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak

		_worklogStateService.Verify(
			s => s.StopTrackingAsync(It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public void Skip_FromBreakToWork_WithAutoStartEnabled_StartsTracking()
	{
		var settings = _settingsService.Object.Settings;
		settings.Pomodoro.AutoStartWorkTracking = true;
		var activeWork = WorkTracker.Domain.Entities.WorkEntry.Create("PROJ-1", DateTime.Now, null, "Test", DateTime.Now);
		_worklogStateService.Setup(s => s.IsTracking).Returns(true);
		_worklogStateService.Setup(s => s.ActiveWork).Returns(activeWork);

		_sut.Start(); // sets _lastTicketId/_lastDescription from ActiveWork
		_worklogStateService.Setup(s => s.IsTracking).Returns(false);

		_sut.Skip(); // Work -> ShortBreak
		_sut.Skip(); // ShortBreak -> Work (should auto-start)

		_worklogStateService.Verify(
			s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
			Times.AtLeastOnce);
	}
}
