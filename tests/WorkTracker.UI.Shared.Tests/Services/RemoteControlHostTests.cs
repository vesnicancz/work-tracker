using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Builders;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.Services;

public class RemoteControlHostTests
{
	private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0);
	private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

	private readonly Mock<IWorklogStateService> _worklogState = new();
	private readonly Mock<IPomodoroService> _pomodoro = new();
	private readonly Mock<IWorkEntryService> _workEntries = new();
	private readonly Mock<IWorkSuggestionOrchestrator> _suggestions = new();
	private readonly Mock<IDialogService> _dialogs = new();
	private readonly Mock<IActionFeedbackService> _feedback = new();
	private readonly Mock<ILocalizationService> _localization = new();
	private readonly RecordingUiDispatcher _uiDispatcher = new();
	private readonly ApplicationSettings _settings = new();
	private readonly RemoteControlHost _host;

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	public RemoteControlHostTests()
	{
		var settingsService = new Mock<ISettingsService>();
		settingsService.Setup(s => s.Settings).Returns(_settings);

		_localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_localization
			.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] args) => $"{key}: {string.Join(", ", args)}");

		_pomodoro.Setup(p => p.GetSnapshot()).Returns(new PomodoroSnapshot(PomodoroPhase.Idle, TimeSpan.Zero, 0, 4, false));
		SetRecentEntries();

		var serviceProvider = new Mock<IServiceProvider>();
		serviceProvider.Setup(p => p.GetService(typeof(IWorkEntryService))).Returns(_workEntries.Object);
		var scope = new Mock<IServiceScope>();
		scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
		var scopeFactory = new Mock<IServiceScopeFactory>();
		scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

		_host = new RemoteControlHost(
			_worklogState.Object,
			_pomodoro.Object,
			settingsService.Object,
			_suggestions.Object,
			_dialogs.Object,
			_feedback.Object,
			_localization.Object,
			scopeFactory.Object,
			_uiDispatcher,
			new FixedTimeProvider(),
			NullLogger<RemoteControlHost>.Instance);
	}

	private static WorkEntry Entry(string? ticketId, DateTime start, string? description = null) =>
		new WorkEntryBuilder().WithTicketId(ticketId).WithStartTime(start).WithDescription(description).Active().Build();

	private void SetRecentEntries(params WorkEntry[] entries)
	{
		_workEntries
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);
	}

	[Fact]
	public async Task GetState_WhileTracking_ReturnsActiveWorkWithLocalOffset()
	{
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", Now.AddHours(-2), "Coding"));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.ActiveWork.Should().Be(new RemoteWorkItem(
			"PROJ-1", "Coding", new DateTimeOffset(Now.AddHours(-2), TimeSpan.FromHours(1))));
		state.LastWork.Should().BeNull();
	}

	[Fact]
	public async Task GetState_WhileIdle_OffersMostRecentWork()
	{
		SetRecentEntries(Entry("OLD-1", Now.AddDays(-2)), Entry("NEW-1", Now.AddHours(-1)), Entry("MID-1", Now.AddDays(-1)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.ActiveWork.Should().BeNull();
		state.LastWork!.TicketId.Should().Be("NEW-1");
	}

	[Fact]
	public async Task GetState_WhileIdle_DoesNotOfferWorkOlderThanMaxAge()
	{
		// Same calendar day as the limit, but before it: the range query returns whole days
		SetRecentEntries(Entry("OLD-1", Now - MaxAge - TimeSpan.FromHours(1)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.LastWork.Should().BeNull();
	}

	[Fact]
	public async Task GetState_WhileIdle_OffersWorkExactlyAtMaxAge()
	{
		SetRecentEntries(Entry("EDGE-1", Now - MaxAge));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.LastWork!.TicketId.Should().Be("EDGE-1");
	}

	[Fact]
	public async Task GetState_QueriesEntriesWithinMaxAge()
	{
		await _host.GetStateAsync(MaxAge, TestToken);

		_workEntries.Verify(s => s.GetWorkEntriesByDateRangeAsync(Now - MaxAge, Now, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ResumeLastWork_WithWorkOlderThanMaxAge_FailsWithoutStarting()
	{
		SetRecentEntries(Entry("OLD-1", Now - MaxAge - TimeSpan.FromHours(1)));

		var result = await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
		_worklogState.Verify(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Theory]
	[InlineData(PomodoroPhase.Idle, StatusIndicatorState.Idle)]
	[InlineData(PomodoroPhase.Work, StatusIndicatorState.Work)]
	[InlineData(PomodoroPhase.ShortBreak, StatusIndicatorState.ShortBreak)]
	[InlineData(PomodoroPhase.LongBreak, StatusIndicatorState.LongBreak)]
	public async Task GetState_MapsPomodoro(PomodoroPhase phase, StatusIndicatorState expected)
	{
		_pomodoro.Setup(p => p.GetSnapshot()).Returns(new PomodoroSnapshot(phase, TimeSpan.FromMinutes(3), 1, 4, true));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.Pomodoro.Should().Be(new RemotePomodoroState(expected, TimeSpan.FromMinutes(3), 1, 4));
	}

	[Fact]
	public void StateChanged_IsRaisedForEverySourceOfChange()
	{
		var raised = 0;
		_host.StateChanged += (_, _) => raised++;

		_worklogState.Raise(s => s.ActiveWorkChanged += null, _worklogState.Object, Entry("PROJ-1", Now));
		_worklogState.Raise(s => s.WorkEntriesModified += null, EventArgs.Empty);
		_pomodoro.Raise(p => p.PhaseChanged += null, _pomodoro.Object, PomodoroPhase.Work);

		raised.Should().Be(3);
	}

	[Fact]
	public async Task StartWork_RunsOnUiThreadAndPassesThrough()
	{
		_worklogState
			.Setup(s => s.StartTrackingAsync("PROJ-1", "Coding", It.IsAny<CancellationToken>()))
			.Callback(() => _uiDispatcher.IsInside.Should().BeTrue())
			.ReturnsAsync(Result.Success(Entry("PROJ-1", Now, "Coding")));

		var result = await _host.StartWorkAsync("PROJ-1", "Coding", TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.StartTrackingAsync("PROJ-1", "Coding", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StopWork_ReportsFailureFromService()
	{
		_worklogState
			.Setup(s => s.StopTrackingAsync(It.IsAny<CancellationToken>()))
			.Callback(() => _uiDispatcher.IsInside.Should().BeTrue())
			.ReturnsAsync(Result.Failure("Nope."));

		var result = await _host.StopWorkAsync(TestToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("Nope.");
	}

	[Fact]
	public async Task ResumeLastWork_StartsTicketAndDescriptionOfMostRecentEntry()
	{
		SetRecentEntries(Entry("PROJ-1", Now.AddHours(-1), "Coding"));
		_worklogState
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry("PROJ-1", Now, "Coding")));

		var result = await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.StartTrackingAsync("PROJ-1", "Coding", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ResumeLastWork_WithoutRecentWork_Fails()
	{
		var result = await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
	}

	[Fact]
	public async Task ShiftActiveWorkStart_UpdatesOnlyTheStart()
	{
		var start = Now.AddHours(-1);
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", start, "Coding"));
		_worklogState
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.Callback(() => _uiDispatcher.IsInside.Should().BeTrue())
			.ReturnsAsync(Result.Success());

		var result = await _host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.UpdateWorkEntryAsync(1, "PROJ-1", start.AddMinutes(-5), null, "Coding", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ShiftActiveWorkStart_IntoTheFuture_FailsWithoutUpdating()
	{
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", Now.AddMinutes(-1)));

		var result = await _host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(5), TestToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.Validation);
		VerifyNoUpdate();
	}

	[Fact]
	public async Task ShiftActiveWorkStart_WhileIdle_FailsWithoutUpdating()
	{
		var result = await _host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), TestToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
		VerifyNoUpdate();
	}

	private static readonly DateTimeOffset MeetingStart = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(1));
	private static readonly DateTimeOffset MeetingEnd = MeetingStart.AddMinutes(30);

	private static OverlapAdjustment Adjustment(OverlapAdjustmentKind kind, DateTime? originalEnd) =>
		new(1, "PROJ-1", null, kind, Now.AddHours(-3), originalEnd, null, null);

	private void SetOverlapPlan(params OverlapAdjustment[] adjustments)
	{
		_worklogState
			.Setup(s => s.ComputeOverlapResolutionAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new OverlapResolutionPlan { Adjustments = adjustments });
	}

	[Fact]
	public async Task AddWorkEntry_WithoutOverlap_CreatesItWithTheGivenTimes()
	{
		SetOverlapPlan();
		_worklogState
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.Callback(() => _uiDispatcher.IsInside.Should().BeTrue())
			.ReturnsAsync(Result.Success(Entry(null, Now)));

		var result = await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, MeetingEnd, TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.CreateWorkEntryAsync(null, MeetingStart.DateTime, "Standup", MeetingEnd.DateTime, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task AddWorkEntry_ConvertsTimesToLocal()
	{
		SetOverlapPlan();
		_worklogState
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry(null, Now)));

		await _host.AddWorkEntryAsync(null, "Standup", MeetingStart.ToUniversalTime(), null, TestToken);

		_worklogState.Verify(s => s.CreateWorkEntryAsync(null, MeetingStart.DateTime, "Standup", null, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task AddWorkEntry_OverlappingActiveWork_ClosesIt()
	{
		SetOverlapPlan(Adjustment(OverlapAdjustmentKind.TrimEnd, null));
		_worklogState
			.Setup(s => s.CreateWorkEntryWithResolutionAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry(null, Now)));

		var result = await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, MeetingEnd, TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.CreateWorkEntryWithResolutionAsync(null, MeetingStart.DateTime, "Standup", MeetingEnd.DateTime, It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Theory]
	[InlineData(OverlapAdjustmentKind.Delete)]
	[InlineData(OverlapAdjustmentKind.Split)]
	[InlineData(OverlapAdjustmentKind.TrimStart)]
	[InlineData(OverlapAdjustmentKind.TrimEnd)]
	public async Task AddWorkEntry_ThatWouldChangeCompletedEntry_FailsWithoutTouchingAnything(OverlapAdjustmentKind kind)
	{
		SetOverlapPlan(Adjustment(kind, Now.AddHours(-1)));

		var result = await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, MeetingEnd, TestToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.Validation);
		_worklogState.Verify(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		_worklogState.Verify(s => s.CreateWorkEntryWithResolutionAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetSuggestions_AsksForTodayAndConvertsItems()
	{
		var start = Now.AddHours(1);
		SetSuggestionGroups(
			Group("O365 Calendar", Suggestion("Standup", null, "O365 Calendar", start, start.AddMinutes(15))),
			Group("Jira", Suggestion("Fix login", "PROJ-1", "Jira")));

		var suggestions = await _host.GetSuggestionsAsync(TestToken);

		_suggestions.Verify(o => o.GetGroupedSuggestionsAsync(Now.Date, It.IsAny<CancellationToken>()), Times.Once);
		suggestions.FailedSources.Should().BeEmpty();
		suggestions.Items.Should().Equal(
			new RemoteSuggestion(
				"Standup",
				null,
				new DateTimeOffset(start, TimeSpan.FromHours(1)),
				new DateTimeOffset(start.AddMinutes(15), TimeSpan.FromHours(1)),
				"O365 Calendar"),
			new RemoteSuggestion("Fix login", "PROJ-1", null, null, "Jira"));
	}

	[Fact]
	public async Task GetSuggestions_WithFailingPlugin_KeepsTheOthersAndNamesTheFailure()
	{
		SetSuggestionGroups(
			Group("O365 Calendar", error: "Not signed in."),
			Group("Jira", Suggestion("Fix login", "PROJ-1", "Jira")));

		var suggestions = await _host.GetSuggestionsAsync(TestToken);

		suggestions.FailedSources.Should().Equal("O365 Calendar");
		suggestions.Items.Should().ContainSingle().Which.TicketId.Should().Be("PROJ-1");
	}

	private void SetSuggestionGroups(params SuggestionGroup[] groups)
	{
		_suggestions
			.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(groups);
	}

	private static SuggestionGroup Group(string name, params WorkSuggestionViewModel[] items) => new()
	{
		PluginId = name,
		PluginName = name,
		Items = items
	};

	private static SuggestionGroup Group(string name, string error) => new()
	{
		PluginId = name,
		PluginName = name,
		Items = [],
		Error = error
	};

	private static WorkSuggestionViewModel Suggestion(string title, string? ticketId, string source, DateTime? start = null, DateTime? end = null) => new()
	{
		Title = title,
		TicketId = ticketId,
		StartTime = start,
		EndTime = end,
		Source = source,
		SourceId = title
	};

	[Fact]
	public void GetFavorites_KeepsOrderAndValues()
	{
		_settings.FavoriteWorkItems =
		[
			new FavoriteWorkItem { Name = "Support", TicketId = "PROJ-2", Description = "Helping out" },
			new FavoriteWorkItem { Name = "Meeting", ShowAsTemplate = true }
		];

		_host.GetFavorites().Should().Equal(
			new RemoteFavorite("Support", "PROJ-2", "Helping out", false),
			new RemoteFavorite("Meeting", null, null, true));
	}

	[Fact]
	public async Task ShowNewWorkEntryDialog_DoesNotWaitForTheDialogToClose()
	{
		var neverClosed = new TaskCompletionSource<bool>();
		var wasOnUiThread = false;
		_dialogs
			.Setup(d => d.ShowNewWorkEntryDialogAsync("PROJ-1", "Coding", null, null, null))
			.Callback(() => wasOnUiThread = _uiDispatcher.IsInside)
			.Returns(neverClosed.Task);

		var result = await _host.ShowNewWorkEntryDialogAsync("PROJ-1", "Coding", TestToken);

		result.IsSuccess.Should().BeTrue();
		wasOnUiThread.Should().BeTrue();
		_dialogs.Verify(d => d.ShowNewWorkEntryDialogAsync("PROJ-1", "Coding", null, null, null), Times.Once);
	}

	[Fact]
	public async Task ShowNewWorkEntryDialog_WhenTheDialogFails_StaysQuiet()
	{
		_dialogs
			.Setup(d => d.ShowNewWorkEntryDialogAsync(null, null, null, null, null))
			.ThrowsAsync(new InvalidOperationException("No window."));

		var act = () => _host.ShowNewWorkEntryDialogAsync(null, null, TestToken);

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task PomodoroCommands_RunOnUiThread()
	{
		_settings.Pomodoro.Enabled = true;
		_pomodoro.Setup(p => p.Start()).Callback(() => _uiDispatcher.IsInside.Should().BeTrue());
		_pomodoro.Setup(p => p.Stop()).Callback(() => _uiDispatcher.IsInside.Should().BeTrue());
		_pomodoro.Setup(p => p.Skip()).Callback(() => _uiDispatcher.IsInside.Should().BeTrue());

		(await _host.StartPomodoroAsync(TestToken)).IsSuccess.Should().BeTrue();
		(await _host.SkipPomodoroPhaseAsync(TestToken)).IsSuccess.Should().BeTrue();
		(await _host.StopPomodoroAsync(TestToken)).IsSuccess.Should().BeTrue();

		_pomodoro.Verify(p => p.Start(), Times.Once);
		_pomodoro.Verify(p => p.Skip(), Times.Once);
		_pomodoro.Verify(p => p.Stop(), Times.Once);
	}

	[Fact]
	public async Task StartPomodoro_WhenTurnedOffInSettings_Fails()
	{
		_settings.Pomodoro.Enabled = false;

		var result = await _host.StartPomodoroAsync(TestToken);

		result.IsFailure.Should().BeTrue();
		_pomodoro.Verify(p => p.Start(), Times.Never);
	}

	[Fact]
	public async Task StartWork_ConfirmsWhatStarted()
	{
		SetStartSucceeds();

		await _host.StartWorkAsync("PROJ-1", "Coding", TestToken);

		VerifySuccessReported("RemoteWorkStarted: PROJ-1 – Coding");
		VerifyNoFailureReported();
	}

	[Fact]
	public async Task StartWork_WithoutTicket_ConfirmsJustTheDescription()
	{
		SetStartSucceeds();

		await _host.StartWorkAsync(null, "Coding", TestToken);

		VerifySuccessReported("RemoteWorkStarted: Coding");
	}

	[Fact]
	public async Task StartWork_WithConfirmationsTurnedOff_StaysQuiet()
	{
		_settings.ShowRemoteControlNotifications = false;
		SetStartSucceeds();

		await _host.StartWorkAsync("PROJ-1", "Coding", TestToken);

		VerifyNothingReported();
	}

	[Fact]
	public async Task StartWork_Failure_IsReportedEvenWithConfirmationsTurnedOff()
	{
		_settings.ShowRemoteControlNotifications = false;
		_worklogState
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("Nope."));

		await _host.StartWorkAsync("PROJ-1", "Coding", TestToken);

		_feedback.Verify(f => f.ReportFailureAsync("Nope.", It.IsAny<CancellationToken>()), Times.Once);
		_feedback.Verify(f => f.ReportSuccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task StartWork_WhenTheServiceThrows_ReportsAndRethrows()
	{
		_worklogState
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Database gone."));

		var act = () => _host.StartWorkAsync("PROJ-1", "Coding", TestToken);

		await act.Should().ThrowAsync<InvalidOperationException>();
		_feedback.Verify(f => f.ReportFailureAsync("RemoteActionFailed: Database gone.", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StartWork_WhenCancelled_ReportsNothing()
	{
		_worklogState
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OperationCanceledException());

		var act = () => _host.StartWorkAsync("PROJ-1", "Coding", TestToken);

		await act.Should().ThrowAsync<OperationCanceledException>();
		VerifyNothingReported();
	}

	[Fact]
	public async Task StopWork_ConfirmsWhatStopped()
	{
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", Now.AddHours(-1), "Coding"));
		_worklogState
			.Setup(s => s.StopTrackingAsync(It.IsAny<CancellationToken>()))
			.Callback(() => _worklogState.Setup(s => s.ActiveWork).Returns((WorkEntry?)null))
			.ReturnsAsync(Result.Success());

		await _host.StopWorkAsync(TestToken);

		VerifySuccessReported("RemoteWorkStopped: PROJ-1 – Coding");
	}

	[Fact]
	public async Task ResumeLastWork_ConfirmsOnce()
	{
		SetRecentEntries(Entry("PROJ-1", Now.AddHours(-1), "Coding"));
		SetStartSucceeds();

		await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		VerifySuccessReported("RemoteWorkStarted: PROJ-1 – Coding");
	}

	[Fact]
	public async Task ResumeLastWork_WithoutRecentWork_ReportsFailure()
	{
		_settings.ShowRemoteControlNotifications = false;

		var result = await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		result.Error.Should().Be("RemoteNoRecentWork");
		_feedback.Verify(f => f.ReportFailureAsync("RemoteNoRecentWork", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ShiftActiveWorkStart_Success_StaysQuiet()
	{
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", Now.AddHours(-1), "Coding"));
		_worklogState
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());

		await _host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), TestToken);

		VerifyNothingReported();
	}

	[Fact]
	public async Task ShiftActiveWorkStart_Failure_IsReported()
	{
		await _host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), TestToken);

		_feedback.Verify(f => f.ReportFailureAsync("RemoteNoActiveWork", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task AddWorkEntry_ConfirmsWhatWasAdded()
	{
		SetOverlapPlan();
		_worklogState
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry(null, Now)));

		await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, MeetingEnd, TestToken);

		VerifySuccessReported("RemoteEntryAdded: Standup");
	}

	[Fact]
	public async Task AddWorkEntry_ThatWouldChangeOtherEntries_ReportsFailure()
	{
		SetOverlapPlan(Adjustment(OverlapAdjustmentKind.Delete, Now.AddHours(-1)));

		await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, MeetingEnd, TestToken);

		_feedback.Verify(f => f.ReportFailureAsync("RemoteEntryWouldChangeOthers", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task ShowNewWorkEntryDialog_ReportsNothing()
	{
		await _host.ShowNewWorkEntryDialogAsync("PROJ-1", "Coding", TestToken);

		VerifyNothingReported();
	}

	[Fact]
	public async Task StartPomodoro_ThatStartsTheTimer_Confirms()
	{
		_settings.Pomodoro.Enabled = true;
		_pomodoro.SetupSequence(p => p.IsRunning).Returns(false).Returns(true);

		await _host.StartPomodoroAsync(TestToken);

		VerifySuccessReported("RemotePomodoroStarted");
	}

	[Fact]
	public async Task StartPomodoro_AlreadyRunning_StaysQuiet()
	{
		_settings.Pomodoro.Enabled = true;
		_pomodoro.Setup(p => p.IsRunning).Returns(true);

		await _host.StartPomodoroAsync(TestToken);

		VerifyNothingReported();
	}

	[Fact]
	public async Task StartPomodoro_WhenTurnedOffInSettings_ReportsFailure()
	{
		_settings.Pomodoro.Enabled = false;

		await _host.StartPomodoroAsync(TestToken);

		_feedback.Verify(f => f.ReportFailureAsync("RemotePomodoroDisabled", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StopPomodoro_ThatStopsTheTimer_Confirms()
	{
		_pomodoro.SetupSequence(p => p.IsRunning).Returns(true).Returns(false);

		await _host.StopPomodoroAsync(TestToken);

		VerifySuccessReported("RemotePomodoroStopped");
	}

	[Fact]
	public async Task SkipPomodoroPhase_LeavesTheNotificationToThePhaseTransition()
	{
		await _host.SkipPomodoroPhaseAsync(TestToken);

		VerifyNothingReported();
	}

	private void SetStartSucceeds()
	{
		_worklogState
			.Setup(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry("PROJ-1", Now, "Coding")));
	}

	private void VerifySuccessReported(string message)
	{
		_feedback.Verify(f => f.ReportSuccessAsync(message, It.IsAny<CancellationToken>()), Times.Once);
		_feedback.Verify(f => f.ReportSuccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	private void VerifyNoFailureReported()
	{
		_feedback.Verify(f => f.ReportFailureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	private void VerifyNothingReported()
	{
		_feedback.Verify(f => f.ReportSuccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		VerifyNoFailureReported();
	}

	private void VerifyNoUpdate()
	{
		_worklogState.Verify(
			s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	private sealed class FixedTimeProvider : TimeProvider
	{
		public override TimeZoneInfo LocalTimeZone { get; } =
			TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(1), "Test", "Test");

		public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now, TimeSpan.FromHours(1)).ToUniversalTime();
	}

	private sealed class RecordingUiDispatcher : IUiDispatcher
	{
		public bool IsInside { get; private set; }

		public async Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			IsInside = true;
			try
			{
				return await action();
			}
			finally
			{
				IsInside = false;
			}
		}
	}
}
