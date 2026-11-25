using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Builders;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public sealed class RemoteControlHostTests : IDisposable
{
	private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0);
	private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

	private readonly Mock<IWorklogStateService> _worklogState = new();
	private readonly Mock<IPomodoroService> _pomodoro = new();
	private readonly Mock<IWorkEntryService> _workEntries = new();
	private readonly Mock<IWorkSuggestionOrchestrator> _suggestions = new();
	private readonly Mock<IWorklogSubmissionService> _submission = new();
	private readonly Mock<IDialogService> _dialogs = new();
	private readonly Mock<IActionFeedbackService> _feedback = new();
	private readonly Mock<ILocalizationService> _localization = new();
	private readonly RecordingUiDispatcher _uiDispatcher = new();
	private readonly FixedTimeProvider _timeProvider = new();
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

		_host = new RemoteControlHost(
			_worklogState.Object,
			_pomodoro.Object,
			settingsService.Object,
			_suggestions.Object,
			_dialogs.Object,
			_feedback.Object,
			_localization.Object,
			_workEntries.Object,
			new DateRangeService(),
			_submission.Object,
			_uiDispatcher,
			_timeProvider,
			NullLogger<RemoteControlHost>.Instance);
	}

	public void Dispose() => _host.Dispose();

	private static WorkEntry Entry(string? ticketId, DateTime start, string? description = null) =>
		new WorkEntryBuilder().WithTicketId(ticketId).WithStartTime(start).WithDescription(description).Active().Build();

	private static WorkEntry Scheduled(int id, DateTime start, DateTime end, string? description = "Standup") =>
		new WorkEntryBuilder().WithId(id).WithTicketId(null).WithDescription(description).WithTimes(start, end).Build();

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
			"PROJ-1", "Coding", new DateTimeOffset(Now.AddHours(-2), TimeSpan.FromHours(1)), null));
		state.LastWork.Should().BeNull();
	}

	[Fact]
	public async Task GetState_WhileIdle_SaysWhenTheLastWorkEnded()
	{
		SetRecentEntries(Scheduled(5, Now.AddDays(-5).AddHours(2), Now.AddDays(-5).AddHours(4)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.LastWork!.EndedAt.Should().Be(new DateTimeOffset(Now.AddDays(-5).AddHours(4), TimeSpan.FromHours(1)));
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

		_workEntries.Verify(s => s.GetWorkEntriesByDateRangeAsync(Now - MaxAge, Now.AddDays(1), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task GetState_DuringScheduledWork_ReturnsItWithLocalOffset()
	{
		SetRecentEntries(Entry("PROJ-1", Now.AddHours(-2), "Coding"), Scheduled(5, Now.AddMinutes(-15), Now.AddMinutes(15)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.ScheduledWork.Should().Be(new RemoteScheduledWork(
			null,
			"Standup",
			new DateTimeOffset(Now.AddMinutes(-15), TimeSpan.FromHours(1)),
			new DateTimeOffset(Now.AddMinutes(15), TimeSpan.FromHours(1))));
	}

	[Fact]
	public async Task GetState_OutsideScheduledWork_ReturnsNone()
	{
		SetRecentEntries(Scheduled(5, Now.AddMinutes(-30), Now), Scheduled(6, Now.AddMinutes(30), Now.AddMinutes(60)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.ScheduledWork.Should().BeNull();
	}

	[Fact]
	public async Task GetState_WhileTracking_ReturnsNoScheduledWork()
	{
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", Now.AddHours(-2), "Coding"));
		SetRecentEntries(Scheduled(5, Now.AddMinutes(-15), Now.AddMinutes(15)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.ScheduledWork.Should().BeNull();
	}

	[Fact]
	public async Task GetState_DoesNotOfferWorkPlannedForLater()
	{
		SetRecentEntries(Entry("PROJ-1", Now.AddHours(-2), "Coding"), Scheduled(5, Now.AddHours(1), Now.AddHours(2)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.LastWork!.TicketId.Should().Be("PROJ-1");
	}

	[Fact]
	public async Task GetState_RaisesStateChangedWhenTheNextScheduledWorkBeginsOrEnds()
	{
		SetRecentEntries(Scheduled(5, Now.AddMinutes(-15), Now.AddMinutes(15)), Scheduled(6, Now.AddMinutes(30), Now.AddMinutes(60)));
		var raised = 0;
		_host.StateChanged += (_, _) => raised++;

		await _host.GetStateAsync(MaxAge, TestToken);

		_timeProvider.Timers.Should().ContainSingle().Which.DueTime.Should().Be(TimeSpan.FromMinutes(15));
		_timeProvider.Timers[0].Fire();
		raised.Should().Be(1);
	}

	[Fact]
	public async Task GetState_WithoutScheduledWork_RaisesStateChangedAtMidnight()
	{
		await _host.GetStateAsync(MaxAge, TestToken);

		_timeProvider.Timers.Should().ContainSingle().Which.DueTime.Should().Be(TimeSpan.FromHours(12));
	}

	[Fact]
	public async Task GetState_ReplacesTheTimerOfThePreviousRead()
	{
		SetRecentEntries(Scheduled(5, Now.AddMinutes(30), Now.AddMinutes(60)));
		await _host.GetStateAsync(MaxAge, TestToken);

		SetRecentEntries();
		await _host.GetStateAsync(MaxAge, TestToken);

		_timeProvider.Timers.Should().HaveCount(2);
		_timeProvider.Timers[0].IsDisposed.Should().BeTrue();
		_timeProvider.Timers[1].IsDisposed.Should().BeFalse();
	}

	[Fact]
	public async Task GetState_SumsTheTimeWorkedTodayAndThisWeek()
	{
		// Now is Wednesday noon
		SetRecentEntries(
			Scheduled(1, new DateTime(2026, 11, 1, 10, 0, 0), new DateTime(2026, 11, 1, 12, 0, 0)), // Sunday, last week
			Scheduled(2, new DateTime(2026, 11, 2, 9, 0, 0), new DateTime(2026, 11, 2, 11, 0, 0)), // Monday
			Scheduled(3, new DateTime(2026, 11, 3, 23, 0, 0), new DateTime(2026, 11, 4, 1, 0, 0)), // over midnight
			Scheduled(4, Now.AddHours(-4), Now.AddHours(-2)),
			Entry("PROJ-1", Now.AddHours(-1), "Coding"),
			Scheduled(5, Now.AddHours(3), Now.AddHours(4))); // later today

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.WorkedTime.Should().Be(new RemoteWorkedTime(TimeSpan.FromHours(4), TimeSpan.FromHours(7), true));
	}

	[Fact]
	public async Task GetState_CountsScheduledWorkInProgressUpToNow()
	{
		SetRecentEntries(Scheduled(5, Now.AddMinutes(-15), Now.AddMinutes(15)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.WorkedTime.Should().Be(new RemoteWorkedTime(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15), true));
	}

	[Fact]
	public async Task GetState_WhileIdle_WorkedTimeIsNotRunning()
	{
		SetRecentEntries(Scheduled(5, Now.AddHours(-2), Now.AddHours(-1)));

		var state = await _host.GetStateAsync(MaxAge, TestToken);

		state.WorkedTime.IsRunning.Should().BeFalse();
	}

	[Fact]
	public async Task GetWorkedTime_ForAnEarlierDay_CountsThatDayOnly()
	{
		// Now is Wednesday noon
		SetRecentEntries(
			Scheduled(1, new DateTime(2026, 11, 1, 23, 0, 0), new DateTime(2026, 11, 2, 1, 0, 0)), // over midnight into Monday
			Scheduled(2, new DateTime(2026, 11, 2, 9, 0, 0), new DateTime(2026, 11, 2, 11, 30, 0)),
			Scheduled(3, new DateTime(2026, 11, 3, 9, 0, 0), new DateTime(2026, 11, 3, 10, 0, 0)));

		var summary = await _host.GetWorkedTimeAsync(new DateOnly(2026, 11, 2), RemoteWorkedTimePeriod.Day, TestToken);

		summary.Should().Be(new RemoteWorkedTimeSummary(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), TimeSpan.FromHours(3.5)));
	}

	[Fact]
	public async Task GetWorkedTime_ForAWeek_CountsMondayToSunday()
	{
		SetRecentEntries(
			Scheduled(1, new DateTime(2026, 10, 25, 9, 0, 0), new DateTime(2026, 10, 25, 10, 0, 0)), // the Sunday before
			Scheduled(2, new DateTime(2026, 10, 26, 9, 0, 0), new DateTime(2026, 10, 26, 11, 0, 0)),
			Scheduled(3, new DateTime(2026, 11, 1, 9, 0, 0), new DateTime(2026, 11, 1, 10, 0, 0)),
			Scheduled(4, new DateTime(2026, 11, 2, 9, 0, 0), new DateTime(2026, 11, 2, 10, 0, 0))); // the Monday after

		var summary = await _host.GetWorkedTimeAsync(new DateOnly(2026, 10, 28), RemoteWorkedTimePeriod.Week, TestToken);

		summary.Should().Be(new RemoteWorkedTimeSummary(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1), TimeSpan.FromHours(3)));
	}

	[Fact]
	public async Task GetWorkedTime_ForToday_CountsUpToNow()
	{
		SetRecentEntries(Entry("PROJ-1", Now.AddHours(-1), "Coding"), Scheduled(5, Now.AddHours(2), Now.AddHours(3)));

		var summary = await _host.GetWorkedTimeAsync(DateOnly.FromDateTime(Now), RemoteWorkedTimePeriod.Day, TestToken);

		summary.Worked.Should().Be(TimeSpan.FromHours(1));
	}

	[Fact]
	public async Task GetWorkedTime_QueriesFromTheDayBefore()
	{
		await _host.GetWorkedTimeAsync(new DateOnly(2026, 11, 2), RemoteWorkedTimePeriod.Week, TestToken);

		_workEntries.Verify(s => s.GetWorkEntriesByDateRangeAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 8), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task GetState_WithShortMaxAge_StillQueriesTheWholeWeek()
	{
		await _host.GetStateAsync(TimeSpan.FromDays(1), TestToken);

		_workEntries.Verify(s => s.GetWorkEntriesByDateRangeAsync(new DateTime(2026, 11, 2), Now.AddDays(1), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StopWork_DuringScheduledWork_EndsItNow()
	{
		var meeting = Scheduled(5, Now.AddMinutes(-15), Now.AddMinutes(15));
		SetRecentEntries(meeting);
		_worklogState
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());

		var result = await _host.StopWorkAsync(TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.UpdateWorkEntryAsync(5, null, meeting.StartTime, Now, "Standup", It.IsAny<CancellationToken>()), Times.Once);
		_worklogState.Verify(s => s.StopTrackingAsync(It.IsAny<CancellationToken>()), Times.Never);
		VerifySuccessReported("RemoteWorkStopped: Standup");
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
	public async Task ResumeLastWork_WhileTracking_FailsWithoutStarting()
	{
		var active = Entry("PROJ-1", Now.AddHours(-1), "Coding");
		_worklogState.Setup(s => s.ActiveWork).Returns(active);
		SetRecentEntries(active);

		var result = await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		result.Error.Should().Be("RemoteWorkAlreadyTracked");
		_worklogState.Verify(s => s.StartTrackingAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task ResumeLastWork_WithoutRecentWork_Fails()
	{
		var result = await _host.ResumeLastWorkAsync(MaxAge, TestToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
	}

	[Fact]
	public async Task ShiftActiveWorkStart_ShiftsWhatIsRunningInTheDatabase()
	{
		var start = Now.AddHours(-1);
		_worklogState.Setup(s => s.ActiveWork).Returns(Entry("PROJ-1", start, "Coding"));
		_worklogState
			.Setup(s => s.ShiftActiveWorkStartAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
			.Callback(() => _uiDispatcher.IsInside.Should().BeTrue())
			.ReturnsAsync(Result.Success());

		var result = await _host.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), TestToken);

		result.IsSuccess.Should().BeTrue();
		_worklogState.Verify(s => s.ShiftActiveWorkStartAsync(TimeSpan.FromMinutes(-5), It.IsAny<CancellationToken>()), Times.Once);
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

	private static SuggestionGroup Group(string name, params WorkSuggestion[] items) => new()
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

	private static WorkSuggestion Suggestion(string title, string? ticketId, string source, DateTime? start = null, DateTime? end = null) => new()
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
			.Setup(s => s.ShiftActiveWorkStartAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
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
	public async Task GetWorkEntries_ReturnsThoseStartingInTheRangeInOrder()
	{
		SetRecentEntries(
			Scheduled(3, Now, Now.AddHours(1)),
			Scheduled(2, Now.AddDays(-1), Now.AddDays(-1).AddHours(1)),
			Scheduled(1, Now.AddDays(-2), Now.AddDays(-1)),
			Scheduled(4, Now.AddDays(1), Now.AddDays(1).AddHours(1)));
		var today = DateOnly.FromDateTime(Now);

		var entries = await _host.GetWorkEntriesAsync(today.AddDays(-1), today, TestToken);

		entries.Select(e => e.Id).Should().Equal(2, 3);
		entries[1].StartTime.Should().Be(new DateTimeOffset(Now, TimeSpan.FromHours(1)));
	}

	[Fact]
	public async Task UpdateWorkEntryText_KeepsTheTimesAndClearsWhatIsMissing()
	{
		var entry = Scheduled(7, Now.AddHours(-2), Now.AddHours(-1));
		_workEntries.Setup(s => s.GetWorkEntryByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
		_worklogState
			.Setup(s => s.UpdateWorkEntryAsync(7, "PROJ-1", entry.StartTime, entry.EndTime, null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());

		var result = await _host.UpdateWorkEntryTextAsync(7, " PROJ-1 ", " ", TestToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TicketId.Should().Be("PROJ-1");
		result.Value.Description.Should().BeNull();
		VerifySuccessReported("RemoteEntryUpdated: PROJ-1");
	}

	[Fact]
	public async Task UpdateWorkEntryText_UnknownEntry_IsNotFound()
	{
		var result = await _host.UpdateWorkEntryTextAsync(7, "PROJ-1", null, TestToken);

		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
		_worklogState.Verify(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	private static DateOnly Today => DateOnly.FromDateTime(Now);

	private void SetUpSubmission(Result<SubmissionResult> result, params WorklogDto[] worklogs)
	{
		_submission.Setup(s => s.GetAvailableProviders()).Returns([new ProviderInfo { Id = "tempo", Name = "Tempo", SupportedModes = WorklogSubmissionMode.Timed }]);
		_submission
			.Setup(s => s.PreviewAsync(It.IsAny<SubmissionPeriod>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([new WorklogSubmissionDto { SubmissionDate = Now.Date, Worklogs = [.. worklogs] }]);
		_submission
			.Setup(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), It.IsAny<string>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(result);
	}

	private static WorklogDto Worklog() => new() { TicketId = "PROJ-1", StartTime = Now, EndTime = Now.AddHours(1), DurationMinutes = 60 };

	private void VerifyNothingSubmitted()
	{
		_submission.Verify(s => s.SubmitCustomWorklogsAsync(It.IsAny<IEnumerable<WorklogDto>>(), It.IsAny<string>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task SubmitWorklogs_SubmitsThePreviewOfThePeriod()
	{
		var worklog = Worklog();
		SetUpSubmission(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }), worklog);

		var result = await _host.SubmitWorklogsAsync(Today, RemoteWorkedTimePeriod.Week, WorklogSubmissionMode.Timed, "tempo", TestToken);

		result.Value!.Successful.Should().Be(1);
		_submission.Verify(s => s.PreviewAsync(SubmissionPeriod.WeekOf(Now.Date), WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()), Times.Once);
		_submission.Verify(s => s.SubmitCustomWorklogsAsync(It.Is<IEnumerable<WorklogDto>>(w => w.Single() == worklog), "tempo", WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()), Times.Once);
		VerifySuccessReported("SubmissionSuccess: 1, Tempo");
	}

	[Fact]
	public async Task SubmitWorklogs_PartlyRefused_ReturnsAndReportsTheErrors()
	{
		SetUpSubmission(
			Result.Success(new SubmissionResult { TotalEntries = 2, SuccessfulEntries = 1, FailedEntries = 1, Errors = [new SubmissionError { ErrorMessage = "No ticket", WorklogIndex = 1 }] }),
			Worklog(), Worklog());

		var result = await _host.SubmitWorklogsAsync(Today, RemoteWorkedTimePeriod.Day, WorklogSubmissionMode.Timed, "tempo", TestToken);

		result.Value!.Errors.Should().Equal(new RemoteSubmissionError(1, "No ticket"));
		_feedback.Verify(f => f.ReportFailureAsync("SubmissionPartial: 1, Tempo, 1", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Theory]
	[InlineData(WorklogSubmissionMode.Aggregated)]
	[InlineData(WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated)]
	public async Task SubmitWorklogs_UnsupportedMode_SubmitsNothing(WorklogSubmissionMode mode)
	{
		SetUpSubmission(Result.Success(new SubmissionResult()), Worklog());

		var result = await _host.SubmitWorklogsAsync(Today, RemoteWorkedTimePeriod.Day, mode, "tempo", TestToken);

		result.ErrorCategory.Should().Be(PluginErrorCategory.Validation);
		VerifyNothingSubmitted();
	}

	[Fact]
	public async Task SubmitWorklogs_UnknownProvider_SubmitsNothing()
	{
		SetUpSubmission(Result.Success(new SubmissionResult()), Worklog());

		var result = await _host.SubmitWorklogsAsync(Today, RemoteWorkedTimePeriod.Day, WorklogSubmissionMode.Timed, "other", TestToken);

		result.ErrorCategory.Should().Be(PluginErrorCategory.NotFound);
		VerifyNothingSubmitted();
	}

	[Fact]
	public async Task SubmitWorklogs_WithoutCompletedWork_SubmitsNothing()
	{
		SetUpSubmission(Result.Success(new SubmissionResult()));

		var result = await _host.SubmitWorklogsAsync(Today, RemoteWorkedTimePeriod.Day, WorklogSubmissionMode.Timed, "tempo", TestToken);

		result.IsFailure.Should().BeTrue();
		VerifyNothingSubmitted();
	}

	[Fact]
	public async Task PreviewWorklogs_ReturnsTheDaysWithLocalOffset()
	{
		SetUpSubmission(Result.Success(new SubmissionResult()), Worklog());

		var result = await _host.PreviewWorklogsAsync(Today, RemoteWorkedTimePeriod.Day, WorklogSubmissionMode.Timed, TestToken);

		var day = result.Value!.Should().ContainSingle().Subject;
		day.Date.Should().Be(Today);
		day.Worklogs.Should().Equal(new RemoteWorklog("PROJ-1", null, new DateTimeOffset(Now, TimeSpan.FromHours(1)), 60));
	}

	[Fact]
	public async Task AddWorkEntry_ConfirmsWhatWasAddedAndWhen()
	{
		SetOverlapPlan();
		_worklogState
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry(null, Now)));

		await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, MeetingEnd, TestToken);

		VerifySuccessReported("RemoteEntryAdded: Standup, 10:00 – 10:30");
	}

	[Fact]
	public async Task AddWorkEntry_WithoutEnd_ConfirmsOnlyTheStart()
	{
		SetOverlapPlan();
		_worklogState
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(Entry(null, Now)));

		await _host.AddWorkEntryAsync(null, "Standup", MeetingStart, null, TestToken);

		VerifySuccessReported("RemoteEntryAdded: Standup, 10:00");
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
			s => s.ShiftActiveWorkStartAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	private sealed class FixedTimeProvider : TimeProvider
	{
		public List<RecordedTimer> Timers { get; } = [];

		public override TimeZoneInfo LocalTimeZone { get; } =
			TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(1), "Test", "Test");

		public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now, TimeSpan.FromHours(1)).ToUniversalTime();

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			var timer = new RecordedTimer(() => callback(state), dueTime);
			Timers.Add(timer);
			return timer;
		}
	}

	private sealed class RecordedTimer(Action callback, TimeSpan dueTime) : ITimer
	{
		public TimeSpan DueTime { get; } = dueTime;

		public bool IsDisposed { get; private set; }

		public void Fire() => callback();

		public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();

		public void Dispose() => IsDisposed = true;

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingUiDispatcher : IUiDispatcher
	{
		public bool IsInside { get; private set; }

		public void Post(Action action) => action();

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
