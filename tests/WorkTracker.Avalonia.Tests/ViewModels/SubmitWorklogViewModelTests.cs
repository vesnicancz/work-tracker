using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.ViewModels;

public class SubmitWorklogViewModelTests
{
	private static readonly DateTime LocalNow = SubmitWorklogViewModelHarness.LocalNow;
	private static readonly ProviderInfo TimedOnlyProvider = SubmitWorklogViewModelHarness.TimedOnlyProvider;
	private static readonly ProviderInfo BothModesProvider = SubmitWorklogViewModelHarness.BothModesProvider;

	private static WorklogDto Worklog(string ticketId = "PROJ-1", int minutes = 60) =>
		new() { TicketId = ticketId, DurationMinutes = minutes };

	private static SubmissionOutcome Delivered(params SubmissionError[] errors) => new(true, "sent", errors);

	private static SubmissionError FailureAt(int index, string message = "Conflict") =>
		new() { ErrorMessage = message, WorklogIndex = index };

	/// <summary>
	/// Answers with the given outcomes one after another and collects what each call was asked to send.
	/// </summary>
	private static List<IReadOnlyList<WorklogDto>> AnswerSubmitWith(SubmitWorklogViewModelHarness harness, params SubmissionOutcome[] outcomes)
	{
		var sent = new List<IReadOnlyList<WorklogDto>>();
		var answers = new Queue<SubmissionOutcome>(outcomes);
		harness.Orchestrator
			.Setup(o => o.SubmitAsync(It.IsAny<IReadOnlyList<WorklogDto>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.Callback((IReadOnlyList<WorklogDto> worklogs, string _, string _, WorklogSubmissionMode _, CancellationToken _) => sent.Add(worklogs))
			.ReturnsAsync(answers.Dequeue);
		return sent;
	}

	private static async Task<SubmitWorklogViewModel> LoadedViewModelAsync(SubmitWorklogViewModelHarness harness, params WorklogDto[] worklogs)
	{
		harness.Worklogs.AddRange(worklogs);
		var vm = harness.CreateViewModel();
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);
		return vm;
	}

	[Fact]
	public void Constructor_NoRememberedProvider_RestoresGlobalModeAndAProviderThatSupportsIt()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.IsAggregatedMode.Should().BeTrue();
		vm.SelectedProvider.Should().Be(BothModesProvider, "Tempo cannot do Aggregated");
	}

	[Fact]
	public void Constructor_TimedMode_ListsAllProvidersAndSelectsFirst()
	{
		var harness = new SubmitWorklogViewModelHarness();

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
		vm.AvailableProviders.Should().HaveCount(2);
		vm.SelectedProvider.Should().Be(TimedOnlyProvider);
	}

	[Fact]
	public void Constructor_RestoresRememberedProviderAndItsOwnMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Timed;
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedProvider.Should().Be(BothModesProvider);
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated, "the mode is remembered per provider");
	}

	[Fact]
	public void Constructor_UnknownRememberedProvider_FallsBackToGlobalMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;
		harness.SettingsModel.LastSubmissionProviderId = "uninstalled-plugin";

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.SelectedProvider.Should().Be(BothModesProvider);
	}

	[Fact]
	public void Constructor_RememberedModeNoLongerSupported_FallsBackToASupportedMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;
		harness.SettingsModel.LastSubmissionProviderId = "tempo";
		harness.SettingsModel.SubmissionModeByProvider["tempo"] = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedProvider.Should().Be(TimedOnlyProvider);
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed, "Tempo only supports Timed");
	}

	[Fact]
	public void Constructor_DoesNotPersistTheRestoredSelection()
	{
		var harness = new SubmitWorklogViewModelHarness();

		using var vm = harness.CreateViewModel();

		harness.SettingsModel.LastSubmissionProviderId.Should().BeNull();
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public void AvailableProviders_AreNeverFilteredByTheSelectedMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		// Tempo cannot do Aggregated, but it stays listed — the provider is the primary choice and
		// hiding it is what would make switching back to it impossible.
		vm.AvailableProviders.Should().HaveCount(2);
		vm.SelectedProvider.Should().Be(BothModesProvider);
	}

	[Fact]
	public void CanUseMode_FollowsWhatTheSelectedProviderSupports()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();

		vm.SelectedProvider.Should().Be(TimedOnlyProvider);
		vm.CanUseTimedMode.Should().BeTrue();
		vm.CanUseAggregatedMode.Should().BeFalse();

		vm.SelectedProvider = BothModesProvider;

		vm.CanUseTimedMode.Should().BeTrue();
		vm.CanUseAggregatedMode.Should().BeTrue();
	}

	[Fact]
	public void ModeChange_UnsupportedByTheSelectedProvider_IsIgnored()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider.Should().Be(TimedOnlyProvider);

		// The dialog disables this radio button; a programmatic set must not slip past it either.
		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
		vm.IsTimedMode.Should().BeTrue();
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public void ModeChange_RemembersTheModeForTheSelectedProvider()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		harness.SettingsModel.LastSubmissionMode.Should().Be(WorklogSubmissionMode.Aggregated);
		harness.SettingsModel.LastSubmissionProviderId.Should().Be("goran");
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should().Be(WorklogSubmissionMode.Aggregated);
	}

	[Fact]
	public void ProviderChange_RestoresTheModeRememberedForThatProvider()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionProviderId = "tempo";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		using var vm = harness.CreateViewModel();
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);

		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.SelectedProvider.Should().Be(BothModesProvider);
	}

	[Fact]
	public void ProviderChange_ToAProviderThatCannotDoTheCurrentMode_SwitchesTheMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		using var vm = harness.CreateViewModel();
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);

		vm.SelectedProvider = TimedOnlyProvider;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should()
			.Be(WorklogSubmissionMode.Aggregated, "leaving Goran must not rewrite its mode");
	}

	[Fact]
	public void ProviderChange_WithoutRememberedMode_KeepsCurrentMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();

		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
	}

	[Fact]
	public void ProviderChange_PersistsTheProviderAndItsMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();

		vm.SelectedProvider = BothModesProvider;

		harness.SettingsModel.LastSubmissionProviderId.Should().Be("goran");
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should().Be(WorklogSubmissionMode.Timed);
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(harness.SettingsModel, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Send_RemembersTheProviderAndModeItWasSentWith()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		AnswerSubmitWith(harness, Delivered());
		using var vm = await LoadedViewModelAsync(harness, Worklog());

		// Nothing in the dialog is touched, so only the submit writes the selection back.
		await vm.SendCommand.ExecuteAsync(null);

		harness.SettingsModel.LastSubmissionProviderId.Should().Be("goran");
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should().Be(WorklogSubmissionMode.Aggregated);
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(harness.SettingsModel, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void SwitchingProvidersBackAndForth_KeepsEachProvidersOwnMode()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();

		// Goran is set to Aggregated...
		vm.SelectedProvider = BothModesProvider;
		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		// ...and picking Tempo, which is Timed-only, switches the mode without touching Goran's.
		vm.SelectedProvider = TimedOnlyProvider;
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);

		// Back to Goran: Aggregated returns.
		vm.SelectedProvider = BothModesProvider;
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
	}

	[Fact]
	public async Task InitializeAsync_LoadsPreviewAndTotals()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.Worklogs.Add(Worklog(minutes: 60));
		harness.Worklogs.Add(Worklog(minutes: 30));
		using var vm = harness.CreateViewModel();

		await vm.InitializeAsync(LocalNow.Date, isWeek: false);

		vm.PreviewItems.Should().HaveCount(2);
		vm.TotalTimeDisplay.Should().Be("1h 30m");
		vm.StatusMessage.Should().Be("ReadyToSubmit");
		vm.IsLoading.Should().BeFalse();
	}

	[Fact]
	public async Task CanSend_RequiresSelectedItemAndProvider()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = harness.CreateViewModel();

		await vm.InitializeAsync(LocalNow.Date, isWeek: false);
		vm.SendCommand.CanExecute(null).Should().BeFalse("there are no preview items");

		harness.Worklogs.Add(Worklog());
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);
		vm.SendCommand.CanExecute(null).Should().BeTrue();

		vm.SelectedProvider = null;
		vm.SendCommand.CanExecute(null).Should().BeFalse("no provider is selected");
	}

	[Fact]
	public async Task Send_AllSucceeded_KeepsDialogOpenAndReportsStatus()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.Orchestrator
			.Setup(o => o.SubmitAsync(It.IsAny<IReadOnlyList<WorklogDto>>(), "tempo", "Tempo", WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Delivered());
		using var vm = await LoadedViewModelAsync(harness, Worklog());
		var closed = false;
		vm.CloseAction = () => closed = true;

		await vm.SendCommand.ExecuteAsync(null);

		closed.Should().BeFalse("the dialog is used for several days in a row");
		vm.HasFailedItems.Should().BeFalse();
		vm.StatusMessage.Should().Be("sent");
		vm.IsSending.Should().BeFalse();
	}

	[Fact]
	public async Task Send_WithFailures_EnablesRetry()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, new SubmissionOutcome(true, "1 failed", [FailureAt(0)]));
		using var vm = await LoadedViewModelAsync(harness, Worklog());
		vm.SubmitLabel.Should().Be("Submit");
		vm.SubmitToolTip.Should().BeNull();

		await vm.SendCommand.ExecuteAsync(null);

		vm.SubmitLabel.Should().Be("ResubmitAll", "the button sends the rows that already went through as well");
		vm.SubmitToolTip.Should().Be("ResubmitAllToolTip");
		vm.HasFailedItems.Should().BeTrue();
		vm.StatusMessage.Should().Be("1 failed");
		vm.RetryFailedCommand.CanExecute(null).Should().BeTrue();
	}

	[Fact]
	public async Task ItemDeselection_RecalculatesTotals()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = await LoadedViewModelAsync(harness, Worklog(minutes: 60), Worklog(minutes: 30));

		vm.PreviewItems[1].IsSelected = false;

		vm.TotalTimeDisplay.Should().Be("1h");
	}

	#region Rows of the preview

	[Fact]
	public async Task Preview_WorklogWithoutATicket_ShowsTheNoTicketLabel()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = await LoadedViewModelAsync(harness, new WorklogDto { TicketId = null, DurationMinutes = 60 });

		var row = vm.PreviewItems.Should().ContainSingle().Subject;
		row.TicketId.Should().BeNull();
		row.TicketIdDisplay.Should().Be("NoTicket");
	}

	[Fact]
	public async Task Preview_Weekly_PutsADateHeaderBeforeEachDay()
	{
		var monday = new DateTime(2025, 1, 13);
		var tuesday = new DateTime(2025, 1, 14);
		var harness = new SubmitWorklogViewModelHarness();
		harness.Orchestrator
			.Setup(o => o.LoadPreviewAsync(It.IsAny<DateTime>(), true, It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				new WorklogSubmissionDto { SubmissionDate = monday, Worklogs = [Worklog("A-1", 60)] },
				new WorklogSubmissionDto { SubmissionDate = tuesday, Worklogs = [Worklog("A-2", 30)] }
			]);
		using var vm = harness.CreateViewModel();

		await vm.InitializeAsync(monday, isWeek: true);

		vm.PreviewItems.Select(i => i.IsDateHeader).Should().Equal(true, false, true, false);
		vm.PreviewItems[0].Date.Should().Be(monday);
		vm.PreviewItems[2].Date.Should().Be(tuesday);
		vm.TotalTimeDisplay.Should().Be("1h 30m", "the headers don't count");
	}

	[Fact]
	public async Task Preview_Daily_HasNoDateHeader()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = await LoadedViewModelAsync(harness, Worklog());

		vm.PreviewItems.Should().ContainSingle().Which.IsDateHeader.Should().BeFalse();
	}

	[Fact]
	public async Task Preview_Aggregated_KeepsTheDurationOverTheTimes()
	{
		var day = LocalNow.Date;
		var harness = new SubmitWorklogViewModelHarness();
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		using var vm = await LoadedViewModelAsync(harness, new WorklogDto
		{
			TicketId = "PROJ-1",
			DurationMinutes = 90,
			StartTime = day.AddHours(9),
			EndTime = day.AddHours(9)
		});

		var row = vm.PreviewItems.Should().ContainSingle().Subject;
		row.IsAggregated.Should().BeTrue();
		row.Duration.Should().Be(90 * 60);
	}

	[Fact]
	public async Task Reset_RestoresTheValuesAndTheSelectionAndClearsErrors()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(0)));
		using var vm = await LoadedViewModelAsync(harness, Worklog("PROJ-1"));
		await vm.SendCommand.ExecuteAsync(null);
		var row = vm.PreviewItems[0];
		row.TicketId = "CHANGED";
		row.IsSelected = false;

		vm.ResetCommand.Execute(null);

		row.TicketId.Should().Be("PROJ-1");
		row.IsSelected.Should().BeTrue();
		row.HasError.Should().BeFalse();
		row.ErrorMessage.Should().BeNull();
		vm.HasFailedItems.Should().BeFalse();
	}

	[Fact]
	public async Task InvertSelection_TogglesTheRowsAndUpdatesTheTotal()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1", 60), Worklog("A-2", 30));
		vm.PreviewItems[1].IsSelected = false;

		vm.InvertSelectionCommand.Execute(null);

		vm.PreviewItems[0].IsSelected.Should().BeFalse();
		vm.PreviewItems[1].IsSelected.Should().BeTrue();
		vm.TotalTimeDisplay.Should().Be("30m");
	}

	[Fact]
	public async Task SelectAll_SelectsEveryRowAndUpdatesTheTotal()
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1", 60), Worklog("A-2", 30));
		vm.PreviewItems[0].IsSelected = false;
		vm.PreviewItems[1].IsSelected = false;

		vm.SelectAllCommand.Execute(null);

		vm.PreviewItems.Should().OnlyContain(i => i.IsSelected);
		vm.TotalTimeDisplay.Should().Be("1h 30m");
	}

	#endregion

	#region Submitting rows

	[Fact]
	public async Task Send_SendsOnlyTheSelectedRowsWithWhatIsInThem()
	{
		var harness = new SubmitWorklogViewModelHarness();
		var sent = AnswerSubmitWith(harness, Delivered());
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1", 60), Worklog("A-2", 30));
		vm.PreviewItems[0].IsSelected = false;
		vm.PreviewItems[1].TicketId = "EDITED";

		await vm.SendCommand.ExecuteAsync(null);

		var worklog = sent.Should().ContainSingle().Subject.Should().ContainSingle().Subject;
		worklog.TicketId.Should().Be("EDITED");
		worklog.DurationMinutes.Should().Be(30);
	}

	[Fact]
	public async Task Send_UncheckedRowBeforeTheFailure_MarksTheRowThatWasSent()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(0, "Conflict")));
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1"), Worklog("A-2"));
		vm.PreviewItems[0].IsSelected = false;

		await vm.SendCommand.ExecuteAsync(null);

		vm.PreviewItems[0].HasError.Should().BeFalse();
		vm.PreviewItems[1].HasError.Should().BeTrue();
		vm.PreviewItems[1].ErrorMessage.Should().Be("Conflict");
	}

	[Fact]
	public async Task Send_RowsThatLookAlike_MarksTheOneThatFailed()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(1)));
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1"), Worklog("A-1"));

		await vm.SendCommand.ExecuteAsync(null);

		vm.PreviewItems[0].HasError.Should().BeFalse();
		vm.PreviewItems[1].HasError.Should().BeTrue();
	}

	[Fact]
	public async Task Send_AfterAFailure_ClearsTheErrorsOfEveryRow()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(0)), Delivered());
		using var vm = await LoadedViewModelAsync(harness, Worklog());
		await vm.SendCommand.ExecuteAsync(null);

		await vm.SendCommand.ExecuteAsync(null);

		vm.PreviewItems[0].HasError.Should().BeFalse();
		vm.PreviewItems[0].ErrorMessage.Should().BeNull();
		vm.HasFailedItems.Should().BeFalse();
	}

	[Fact]
	public async Task Send_ProviderGaveNoAnswer_KeepsWhatWasKnownAboutTheRows()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(0)), new SubmissionOutcome(false, "Network error", []));
		using var vm = await LoadedViewModelAsync(harness, Worklog());
		await vm.SendCommand.ExecuteAsync(null);

		await vm.SendCommand.ExecuteAsync(null);

		vm.PreviewItems[0].HasError.Should().BeTrue();
		vm.HasFailedItems.Should().BeTrue();
		vm.StatusMessage.Should().Be("Network error");
	}

	[Fact]
	public async Task Send_ErrorWithoutAPosition_MarksNoRow()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, new SubmissionOutcome(true, "partial", [new SubmissionError { ErrorMessage = "Conflict" }]));
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1"), Worklog("A-2"));

		await vm.SendCommand.ExecuteAsync(null);

		vm.PreviewItems.Should().OnlyContain(i => !i.HasError);
		vm.HasFailedItems.Should().BeFalse();
		vm.StatusMessage.Should().Be("partial");
	}

	[Fact]
	public async Task Retry_SendsOnlyTheSelectedFailedRows()
	{
		var harness = new SubmitWorklogViewModelHarness();
		var sent = AnswerSubmitWith(harness, Delivered(FailureAt(1), FailureAt(2)), Delivered());
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1"), Worklog("A-2"), Worklog("A-3"));
		await vm.SendCommand.ExecuteAsync(null);
		vm.PreviewItems[2].IsSelected = false;

		await vm.RetryFailedCommand.ExecuteAsync(null);

		sent[1].Select(w => w.TicketId).Should().Equal("A-2");
	}

	[Fact]
	public async Task Retry_KeepsTheErrorOfARowItDidNotSend()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(0), FailureAt(1)), Delivered());
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1"), Worklog("A-2"));
		await vm.SendCommand.ExecuteAsync(null);
		vm.PreviewItems[1].IsSelected = false;

		await vm.RetryFailedCommand.ExecuteAsync(null);

		vm.PreviewItems[0].HasError.Should().BeFalse();
		vm.PreviewItems[1].HasError.Should().BeTrue();
		vm.HasFailedItems.Should().BeTrue();
	}

	[Fact]
	public async Task Retry_FailureOfASentRow_MarksThatRowByItsPositionAmongTheSent()
	{
		var harness = new SubmitWorklogViewModelHarness();
		AnswerSubmitWith(harness, Delivered(FailureAt(1)), Delivered(FailureAt(0, "Still failing")));
		using var vm = await LoadedViewModelAsync(harness, Worklog("A-1"), Worklog("A-2"));
		await vm.SendCommand.ExecuteAsync(null);

		await vm.RetryFailedCommand.ExecuteAsync(null);

		vm.PreviewItems[0].HasError.Should().BeFalse();
		vm.PreviewItems[1].ErrorMessage.Should().Be("Still failing");
	}

	#endregion
}
