using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Domain.Entities;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.ViewModels;

/// <summary>
/// Mocked collaborators for a <see cref="MainViewModel"/>, shared by the view-model tests and by the
/// window smoke tests that need a real view model behind a <see cref="Views.MainWindow"/>.
/// </summary>
internal sealed class MainViewModelHarness
{
	internal static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	public Mock<IDialogService> Dialogs { get; } = new();
	public Mock<INotificationService> Notifications { get; } = new();
	public Mock<IWorklogStateService> WorklogState { get; } = new();
	public Mock<IWorkEntryEditOrchestrator> EditOrchestrator { get; } = new();
	public Mock<IWorkSuggestionOrchestrator> SuggestionOrchestrator { get; } = new();
	public Mock<IPomodoroService> PomodoroService { get; } = new();
	public Mock<ISettingsService> Settings { get; } = new();
	public Mock<ILocalizationService> Localization { get; } = new();
	public Mock<IWorkEntryService> WorkEntryService { get; } = new();
	public Mock<IUiDispatcher> UiDispatcher { get; } = new();
	public List<WorkEntry> Entries { get; } = new();

	public MainViewModelHarness()
	{
		UiDispatcher.Setup(d => d.Post(It.IsAny<Action>())).Callback<Action>(action => action());
		Settings.SetupGet(s => s.Settings).Returns(new ApplicationSettings());
		Localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		Localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
		Localization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] _) => key);
		PomodoroService.Setup(p => p.GetSnapshot())
			.Returns(new PomodoroSnapshot(PomodoroPhase.Work, TimeSpan.FromMinutes(25), 0, 4, false));
		WorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(() => Entries);
	}

	public MainViewModel CreateViewModel()
	{
		var timeProvider = new Mock<TimeProvider> { CallBase = true };
		timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(LocalNow, TimeSpan.Zero));
		timeProvider.SetupGet(t => t.LocalTimeZone).Returns(TimeZoneInfo.Utc);

		return new MainViewModel(
			WorkEntryService.Object,
			Dialogs.Object,
			Notifications.Object,
			WorklogState.Object,
			EditOrchestrator.Object,
			SuggestionOrchestrator.Object,
			PomodoroService.Object,
			Settings.Object,
			Localization.Object,
			timeProvider.Object,
			UiDispatcher.Object,
			NullLogger<MainViewModel>.Instance);
	}
}
