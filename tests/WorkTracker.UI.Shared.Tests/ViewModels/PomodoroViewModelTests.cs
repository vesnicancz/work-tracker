using FluentAssertions;
using Moq;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class PomodoroViewModelTests
{
	private readonly Mock<IPomodoroService> _pomodoroService = new();
	private readonly Mock<ISettingsService> _settingsService = new();
	private readonly ApplicationSettings _settings = new();

	private PomodoroViewModel CreateViewModel()
	{
		_settingsService.SetupGet(s => s.Settings).Returns(_settings);

		var localization = new Mock<ILocalizationService>();
		localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);

		return new PomodoroViewModel(_pomodoroService.Object, _settingsService.Object, localization.Object);
	}

	private void SetupSnapshot(
		PomodoroPhase phase = PomodoroPhase.Work,
		int completed = 0,
		int beforeLongBreak = 4,
		bool isRunning = false,
		int remainingSeconds = 1500)
	{
		_pomodoroService.Setup(p => p.GetSnapshot()).Returns(new PomodoroSnapshot(
			phase, TimeSpan.FromSeconds(remainingSeconds), completed, beforeLongBreak, isRunning));
	}

	[Fact]
	public void UpdatePhase_Work_SetsFlagsDisplayAndCount()
	{
		SetupSnapshot(phase: PomodoroPhase.Work, completed: 2, beforeLongBreak: 4, isRunning: true, remainingSeconds: 25 * 60);
		var vm = CreateViewModel();

		vm.UpdatePhase(PomodoroPhase.Work);

		vm.IsWork.Should().BeTrue();
		vm.IsShortBreak.Should().BeFalse();
		vm.IsLongBreak.Should().BeFalse();
		vm.IsRunning.Should().BeTrue();
		vm.PhaseDisplay.Should().Be("PomodoroWork");
		vm.Count.Should().Be("2/4");
		vm.TimeRemaining.Should().Be("25:00");
	}

	[Theory]
	[InlineData(PomodoroPhase.ShortBreak, "PomodoroShortBreak")]
	[InlineData(PomodoroPhase.LongBreak, "PomodoroLongBreak")]
	public void UpdatePhase_Breaks_SetMatchingFlagAndDisplay(PomodoroPhase phase, string expectedDisplay)
	{
		SetupSnapshot(phase: phase);
		var vm = CreateViewModel();

		vm.UpdatePhase(phase);

		vm.PhaseDisplay.Should().Be(expectedDisplay);
		vm.IsShortBreak.Should().Be(phase == PomodoroPhase.ShortBreak);
		vm.IsLongBreak.Should().Be(phase == PomodoroPhase.LongBreak);
		vm.IsWork.Should().BeFalse();
	}

	[Fact]
	public void UpdateTimeDisplay_FormatsMinutesAndSeconds()
	{
		_pomodoroService.SetupGet(p => p.TimeRemaining).Returns(new TimeSpan(0, 4, 7));
		var vm = CreateViewModel();

		vm.UpdateTimeDisplay();

		vm.TimeRemaining.Should().Be("04:07");
	}

	[Fact]
	public void Commands_DelegateToService()
	{
		var vm = CreateViewModel();

		vm.StartCommand.Execute(null);
		vm.StopCommand.Execute(null);
		vm.SkipPhaseCommand.Execute(null);

		_pomodoroService.Verify(p => p.Start(), Times.Once);
		_pomodoroService.Verify(p => p.Stop(), Times.Once);
		_pomodoroService.Verify(p => p.Skip(), Times.Once);
	}

	[Fact]
	public void ServiceEvents_AreReRaisedForPlatformMarshalling()
	{
		var vm = CreateViewModel();
		PomodoroPhase? phaseRaised = null;
		var tickRaised = false;
		vm.PhaseChangedOnService += (_, phase) => phaseRaised = phase;
		vm.TickOnService += (_, _) => tickRaised = true;

		_pomodoroService.Raise(p => p.PhaseChanged += null, _pomodoroService.Object, PomodoroPhase.ShortBreak);
		_pomodoroService.Raise(p => p.Tick += null, _pomodoroService.Object, EventArgs.Empty);

		phaseRaised.Should().Be(PomodoroPhase.ShortBreak);
		tickRaised.Should().BeTrue();
	}

	[Fact]
	public void IsEnabled_ReflectsSettings()
	{
		_settings.Pomodoro.Enabled = true;
		var vm = CreateViewModel();

		vm.IsEnabled.Should().BeTrue();

		_settings.Pomodoro.Enabled = false;
		var raised = new List<string?>();
		vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

		vm.RefreshEnabled();

		vm.IsEnabled.Should().BeFalse();
		raised.Should().Contain(nameof(PomodoroViewModel.IsEnabled));
	}

	[Fact]
	public void Dispose_UnsubscribesFromServiceEvents()
	{
		var vm = CreateViewModel();
		var phaseRaised = false;
		vm.PhaseChangedOnService += (_, _) => phaseRaised = true;

		vm.Dispose();
		_pomodoroService.Raise(p => p.PhaseChanged += null, _pomodoroService.Object, PomodoroPhase.Work);

		phaseRaised.Should().BeFalse();
	}
}
