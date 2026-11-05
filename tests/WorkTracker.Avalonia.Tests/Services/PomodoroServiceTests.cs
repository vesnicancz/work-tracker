using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class PomodoroServiceTests : IAsyncLifetime
{
	private readonly Mock<ISettingsService> _settingsService = new();
	private readonly Mock<INotificationService> _notificationService = new();
	private readonly Mock<IWorklogStateService> _worklogStateService = new();
	private readonly Mock<IPluginManager> _pluginManager = new();
	private readonly Mock<IStatusIndicatorPlugin> _statusIndicatorPlugin = new();
	private readonly Mock<ISystemNotificationService> _systemNotification = new();
	private readonly Mock<ILocalizationService> _localization = new();
	private readonly Mock<IActionFeedbackService> _actionFeedback = new();
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
		_worklogStateService.Setup(s => s.StopTrackingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
		_worklogStateService
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(WorkTracker.Domain.Entities.WorkEntry.Create("PROJ-1", DateTime.Now, null, "Test", DateTime.Now)));
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
			_actionFeedback.Object,
			TimeProvider.System,
			NullLogger<PomodoroService>.Instance);
	}

	public ValueTask InitializeAsync() => ValueTask.CompletedTask;

	public async ValueTask DisposeAsync()
	{
		await _sut.DisposeAsync();
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
	public async Task Dispose_DuringAPomodoro_TurnsTheIndicatorOff()
	{
		_sut.Start();
		_statusIndicatorPlugin.Invocations.Clear();

		await _sut.DisposeAsync();

		_statusIndicatorPlugin.Verify(p => p.SetStateAsync(StatusIndicatorState.Idle, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Dispose_OutsideAPomodoro_LeavesTheIndicatorAlone()
	{
		await _sut.DisposeAsync();

		_statusIndicatorPlugin.Verify(p => p.SetStateAsync(It.IsAny<StatusIndicatorState>(), It.IsAny<CancellationToken>()), Times.Never);
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
	public void Skip_WithSystemNotificationsTurnedOff_KeepsOnlyTheToast()
	{
		_settingsService.Object.Settings.Pomodoro.ShowSystemNotifications = false;
		_sut.Start();

		_sut.Skip(); // Work -> ShortBreak

		_systemNotification.Verify(
			n => n.ShowNotificationAsync(It.IsAny<string>(), It.IsAny<string>()),
			Times.Never);
		_notificationService.Verify(n => n.ShowSuccess("PomodoroCompleted"), Times.Once);
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
	public void StatusIndicatorStartedDuringAPomodoro_GetsTheCurrentPhase()
	{
		// It was enabled or reconfigured just now, or a connection test put it aside for a moment
		_sut.Start();
		var started = new Mock<IStatusIndicatorPlugin>();

		_pluginManager.Raise(p => p.PluginStarted += null, _pluginManager.Object, (IPlugin)started.Object);

		started.Verify(p => p.SetStateAsync(StatusIndicatorState.Work, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void StatusIndicatorStartedWithoutAPomodoro_IsLeftAsItIs()
	{
		var started = new Mock<IStatusIndicatorPlugin>();

		_pluginManager.Raise(p => p.PluginStarted += null, _pluginManager.Object, (IPlugin)started.Object);

		started.Verify(p => p.SetStateAsync(It.IsAny<StatusIndicatorState>(), It.IsAny<CancellationToken>()), Times.Never);
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
	public void Skip_FromWorkToBreak_WhenAutoStopFails_ReportsIt()
	{
		var settings = _settingsService.Object.Settings;
		settings.Pomodoro.AutoStopWorkTracking = true;
		_worklogStateService.Setup(s => s.IsTracking).Returns(true);
		_worklogStateService
			.Setup(s => s.StopTrackingAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure("Overlap"));
		_localization
			.Setup(l => l.GetFormattedString("PomodoroAutoStopFailed", It.IsAny<object[]>()))
			.Returns("auto stop failed");

		_sut.Start();
		_sut.Skip(); // Work -> ShortBreak

		_actionFeedback.Verify(f => f.ReportFailureAsync("auto stop failed", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void Skip_FromBreakToWork_WhenAutoStartFails_ReportsIt()
	{
		var settings = _settingsService.Object.Settings;
		settings.Pomodoro.AutoStartWorkTracking = true;
		var activeWork = WorkTracker.Domain.Entities.WorkEntry.Create("PROJ-1", DateTime.Now, null, "Test", DateTime.Now);
		_worklogStateService.Setup(s => s.IsTracking).Returns(true);
		_worklogStateService.Setup(s => s.ActiveWork).Returns(activeWork);
		_worklogStateService
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkTracker.Domain.Entities.WorkEntry>("Overlap"));
		_localization
			.Setup(l => l.GetFormattedString("PomodoroAutoStartFailed", It.IsAny<object[]>()))
			.Returns("auto start failed");

		_sut.Start();
		_worklogStateService.Setup(s => s.IsTracking).Returns(false);

		_sut.Skip(); // Work -> ShortBreak
		_sut.Skip(); // ShortBreak -> Work (auto-start fails)

		_actionFeedback.Verify(f => f.ReportFailureAsync("auto start failed", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
