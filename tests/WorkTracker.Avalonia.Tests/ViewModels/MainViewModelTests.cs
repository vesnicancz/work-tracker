using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Domain.Entities;
using WorkTracker.Tests.Common.Builders;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Tests.ViewModels;

public class MainViewModelTests
{
	private static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	private sealed class Harness
	{
		public Mock<IDialogService> Dialogs { get; } = new();
		public Mock<INotificationService> Notifications { get; } = new();
		public Mock<IWorklogStateService> WorklogState { get; } = new();
		public Mock<IWorkEntryEditOrchestrator> EditOrchestrator { get; } = new();
		public Mock<IWorkSuggestionOrchestrator> SuggestionOrchestrator { get; } = new();
		public Mock<IPomodoroService> PomodoroService { get; } = new();
		public Mock<ISettingsService> Settings { get; } = new();
		public Mock<ILocalizationService> Localization { get; } = new();
		public Mock<IWorkEntryService> WorkEntryService { get; } = new();
		public List<WorkEntry> Entries { get; } = new();

		public Harness()
		{
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
			var serviceProvider = new Mock<IServiceProvider>();
			serviceProvider.Setup(p => p.GetService(typeof(IWorkEntryService))).Returns(WorkEntryService.Object);
			var scope = new Mock<IServiceScope>();
			scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);
			var scopeFactory = new Mock<IServiceScopeFactory>();
			scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

			var timeProvider = new Mock<TimeProvider>();
			timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(LocalNow, TimeSpan.Zero));
			timeProvider.SetupGet(t => t.LocalTimeZone).Returns(TimeZoneInfo.Utc);

			return new MainViewModel(
				scopeFactory.Object,
				Dialogs.Object,
				Notifications.Object,
				WorklogState.Object,
				EditOrchestrator.Object,
				SuggestionOrchestrator.Object,
				PomodoroService.Object,
				Settings.Object,
				Localization.Object,
				timeProvider.Object,
				NullLogger<MainViewModel>.Instance);
		}
	}

	[Fact]
	public Task Constructor_LoadsEntriesForTodayAndComputesTotal() => UiThread.Dispatch(() =>
	{
		var harness = new Harness();
		harness.Entries.Add(new WorkEntryBuilder().WithId(1).WithTimes(9, 10).Build());
		harness.Entries.Add(new WorkEntryBuilder().WithId(2).WithTimes(10, 12).Build());

		using var vm = harness.CreateViewModel();

		vm.SelectedDate.Should().Be(LocalNow.Date);
		harness.WorkEntryService.Verify(
			s => s.GetWorkEntriesByDateAsync(LocalNow.Date, It.IsAny<CancellationToken>()), Times.Once);
		vm.WorkEntries.Should().HaveCount(2);
		vm.TotalDayDuration.Should().Be("03:00:00");
	});

	[Fact]
	public Task WorkInput_WithTicket_DetectsTicketAndDescription() => UiThread.Dispatch(() =>
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.WorkInput = "PROJ-123 Fix the bug";

		vm.DetectedTicketId.Should().Be("PROJ-123");
		vm.DetectedDescription.Should().Be("Fix the bug");
	});

	[Fact]
	public Task WorkInput_WithoutTicket_DetectsDescriptionOnly() => UiThread.Dispatch(() =>
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.WorkInput = "just some work";

		vm.DetectedTicketId.Should().BeNull();
		vm.DetectedDescription.Should().Be("just some work");
	});

	[Fact]
	public Task StartWorkCommand_CanExecute_RequiresNonEmptyInput() => UiThread.Dispatch(() =>
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.StartWorkCommand.CanExecute(null).Should().BeFalse();

		vm.WorkInput = "PROJ-1 Something";

		vm.StartWorkCommand.CanExecute(null).Should().BeTrue();
	});

	[Fact]
	public Task StartWork_Success_ClearsInputAndNotifies() => UiThread.Dispatch(async () =>
	{
		var harness = new Harness();
		harness.WorklogState
			.Setup(s => s.StartTrackingAsync("PROJ-1", "Something", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new WorkEntryBuilder().Active().Build()));
		using var vm = harness.CreateViewModel();
		vm.WorkInput = "PROJ-1 Something";

		await vm.StartWorkCommand.ExecuteAsync(null);

		harness.WorklogState.Verify(
			s => s.StartTrackingAsync("PROJ-1", "Something", It.IsAny<CancellationToken>()), Times.Once);
		vm.WorkInput.Should().BeEmpty();
		harness.Notifications.Verify(n => n.ShowSuccess(It.IsAny<string>()), Times.Once);
	});

	[Fact]
	public Task StartWork_Failure_ShowsErrorAndKeepsInput() => UiThread.Dispatch(async () =>
	{
		var harness = new Harness();
		harness.WorklogState
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("overlap detected"));
		using var vm = harness.CreateViewModel();
		vm.WorkInput = "PROJ-1 Something";

		await vm.StartWorkCommand.ExecuteAsync(null);

		harness.Dialogs.Verify(d => d.ShowErrorAsync("overlap detected", It.IsAny<string>()), Times.Once);
		vm.WorkInput.Should().Be("PROJ-1 Something");
		harness.Notifications.Verify(n => n.ShowSuccess(It.IsAny<string>()), Times.Never);
	});

	[Fact]
	public Task StopWork_Success_Notifies() => UiThread.Dispatch(async () =>
	{
		var harness = new Harness();
		harness.WorklogState.SetupGet(s => s.IsTracking).Returns(true);
		harness.WorklogState
			.Setup(s => s.StopTrackingAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());
		using var vm = harness.CreateViewModel();

		vm.StopWorkCommand.CanExecute(null).Should().BeTrue();
		await vm.StopWorkCommand.ExecuteAsync(null);

		harness.WorklogState.Verify(s => s.StopTrackingAsync(It.IsAny<CancellationToken>()), Times.Once);
		harness.Notifications.Verify(n => n.ShowSuccess(It.IsAny<string>()), Times.Once);
	});

	[Fact]
	public Task IsTrackingChanged_RaisesPropertyChangedAndUpdatesCanExecute() => UiThread.Dispatch(() =>
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();
		var raised = new List<string?>();
		vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

		vm.StopWorkCommand.CanExecute(null).Should().BeFalse();

		harness.WorklogState.SetupGet(s => s.IsTracking).Returns(true);
		harness.WorklogState.Raise(s => s.IsTrackingChanged += null, harness.WorklogState.Object, true);

		raised.Should().Contain(nameof(MainViewModel.IsTracking));
		vm.StopWorkCommand.CanExecute(null).Should().BeTrue();
	});

	[Fact]
	public Task DeleteWorkEntry_Confirmed_DeletesAndNotifies() => UiThread.Dispatch(async () =>
	{
		var harness = new Harness();
		var entry = new WorkEntryBuilder().WithId(42).WithTimes(9, 10).Build();
		harness.Dialogs
			.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(true);
		harness.WorklogState
			.Setup(s => s.DeleteWorkEntryAsync(42, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());
		using var vm = harness.CreateViewModel();

		await ((IAsyncRelayCommand<WorkEntry>)vm.DeleteWorkEntryCommand).ExecuteAsync(entry);

		harness.WorklogState.Verify(s => s.DeleteWorkEntryAsync(42, It.IsAny<CancellationToken>()), Times.Once);
		harness.Notifications.Verify(n => n.ShowSuccess(It.IsAny<string>()), Times.Once);
	});

	[Fact]
	public Task DeleteWorkEntry_Declined_DoesNothing() => UiThread.Dispatch(async () =>
	{
		var harness = new Harness();
		var entry = new WorkEntryBuilder().WithId(42).WithTimes(9, 10).Build();
		harness.Dialogs
			.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(false);
		using var vm = harness.CreateViewModel();

		await ((IAsyncRelayCommand<WorkEntry>)vm.DeleteWorkEntryCommand).ExecuteAsync(entry);

		harness.WorklogState.Verify(
			s => s.DeleteWorkEntryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
	});

	[Fact]
	public Task DayNavigation_ChangesSelectedDateAndRefreshes() => UiThread.Dispatch(() =>
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.PreviousDayCommand.Execute(null);
		vm.SelectedDate.Should().Be(LocalNow.Date.AddDays(-1));

		vm.GoToTodayCommand.Execute(null);
		vm.SelectedDate.Should().Be(LocalNow.Date);

		vm.NextDayCommand.Execute(null);
		vm.SelectedDate.Should().Be(LocalNow.Date.AddDays(1));

		harness.WorkEntryService.Verify(
			s => s.GetWorkEntriesByDateAsync(LocalNow.Date.AddDays(-1), It.IsAny<CancellationToken>()), Times.Once);
		harness.WorkEntryService.Verify(
			s => s.GetWorkEntriesByDateAsync(LocalNow.Date.AddDays(1), It.IsAny<CancellationToken>()), Times.Once);
	});

	[Fact]
	public Task StartWorkFromHistory_CreatesNewEntryStartingNow() => UiThread.Dispatch(async () =>
	{
		var harness = new Harness();
		var entry = new WorkEntryBuilder().WithId(7).WithTicketId("PROJ-7").WithDescription("Old work").WithTimes(9, 10).Build();
		harness.EditOrchestrator
			.Setup(o => o.SaveNewAsync("PROJ-7", LocalNow, null, "Old work", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(true));
		using var vm = harness.CreateViewModel();

		await ((IAsyncRelayCommand<WorkEntry>)vm.StartWorkFromHistoryCommand).ExecuteAsync(entry);

		harness.EditOrchestrator.Verify(
			o => o.SaveNewAsync("PROJ-7", LocalNow, null, "Old work", It.IsAny<CancellationToken>()), Times.Once);
		harness.Notifications.Verify(n => n.ShowSuccess(It.IsAny<string>()), Times.Once);
	});
}
