using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Tests.Common.Builders;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Tests.ViewModels;

public class WorkEntryEditViewModelTests
{
	private static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	private readonly Mock<IWorkEntryEditOrchestrator> _orchestrator = new();
	private readonly Mock<ILocalizationService> _localization = new();

	private WorkEntryEditViewModel CreateViewModel()
	{
		_localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_localization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] _) => key);

		var timeProvider = new Mock<TimeProvider>();
		timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(LocalNow, TimeSpan.Zero));
		timeProvider.SetupGet(t => t.LocalTimeZone).Returns(TimeZoneInfo.Utc);

		return new WorkEntryEditViewModel(
			_orchestrator.Object,
			_localization.Object,
			timeProvider.Object,
			NullLogger<WorkEntryEditViewModel>.Instance);
	}

	[Fact]
	public void InitializeForNew_WithoutArguments_UsesRoundedNowWithoutEnd()
	{
		var vm = CreateViewModel();

		vm.InitializeForNew();

		vm.IsNewEntry.Should().BeTrue();
		vm.DialogTitle.Should().Be("NewWorkEntry");
		vm.StartDate.Should().Be(LocalNow.Date);
		vm.StartTime.Should().Be(new TimeSpan(12, 0, 0));
		vm.HasEndTime.Should().BeFalse();
		vm.EndDateTime.Should().BeNull();
	}

	[Fact]
	public void InitializeForNew_WithSuggestionValues_PopulatesFields()
	{
		var vm = CreateViewModel();

		vm.InitializeForNew(
			ticketId: "PROJ-5",
			description: "Meeting",
			startTime: new DateTime(2026, 1, 10, 9, 0, 0),
			endTime: new DateTime(2026, 1, 10, 10, 30, 0));

		vm.TicketId.Should().Be("PROJ-5");
		vm.Description.Should().Be("Meeting");
		vm.StartDateTime.Should().Be(new DateTime(2026, 1, 10, 9, 0, 0));
		vm.HasEndTime.Should().BeTrue();
		vm.EndDateTime.Should().Be(new DateTime(2026, 1, 10, 10, 30, 0));
	}

	[Fact]
	public void InitializeForEdit_PopulatesFieldsFromEntry()
	{
		var vm = CreateViewModel();
		var entry = new WorkEntryBuilder()
			.WithId(7)
			.WithTicketId("PROJ-7")
			.WithDescription("Old work")
			.WithTimes(new DateTime(2026, 1, 10, 9, 0, 0), new DateTime(2026, 1, 10, 17, 0, 0))
			.Build();

		vm.InitializeForEdit(entry);

		vm.IsNewEntry.Should().BeFalse();
		vm.DialogTitle.Should().Be("EditWorkEntry");
		vm.EntryId.Should().Be(7);
		vm.TicketId.Should().Be("PROJ-7");
		vm.StartDateTime.Should().Be(new DateTime(2026, 1, 10, 9, 0, 0));
		vm.EndDateTime.Should().Be(new DateTime(2026, 1, 10, 17, 0, 0));
	}

	[Fact]
	public void HasEndTime_WhenNowEqualsStart_DefaultsEndToStartPlusHour()
	{
		var vm = CreateViewModel();
		vm.InitializeForNew();

		vm.HasEndTime = true;

		// Rounded "now" (12:00) equals the start time, so the candidate end moves an hour ahead
		vm.EndDateTime.Should().Be(LocalNow.Date.AddHours(13));
	}

	[Fact]
	public void HasEndTime_Disabled_ClearsEndValues()
	{
		var vm = CreateViewModel();
		vm.InitializeForNew(endTime: new DateTime(2026, 1, 15, 14, 0, 0));

		vm.HasEndTime = false;

		vm.EndDate.Should().BeNull();
		vm.EndTime.Should().BeNull();
		vm.EndDateTime.Should().BeNull();
	}

	[Fact]
	public void ValidationError_DisablesSaveCommand()
	{
		_orchestrator
			.Setup(o => o.Validate(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>()))
			.Returns("end before start");
		var vm = CreateViewModel();

		vm.InitializeForNew();

		vm.ValidationError.Should().Be("end before start");
		vm.SaveCommand.CanExecute(null).Should().BeFalse();
	}

	[Fact]
	public async Task Save_NewEntry_CallsOrchestratorAndCloses()
	{
		_orchestrator
			.Setup(o => o.SaveNewAsync("PROJ-1", It.IsAny<DateTime>(), It.IsAny<DateTime?>(), "Fix", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(true));
		var vm = CreateViewModel();
		var closed = false;
		vm.CloseAction = () => closed = true;
		vm.InitializeForNew(ticketId: "PROJ-1", description: "Fix");

		await vm.SaveCommand.ExecuteAsync(null);

		_orchestrator.Verify(o => o.SaveNewAsync(
			"PROJ-1", vm.StartDateTime, null, "Fix", It.IsAny<CancellationToken>()), Times.Once);
		vm.DialogResult.Should().BeTrue();
		closed.Should().BeTrue();
	}

	[Fact]
	public async Task Save_ExistingEntry_CallsSaveExisting()
	{
		_orchestrator
			.Setup(o => o.SaveExistingAsync(7, It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(true));
		var vm = CreateViewModel();
		vm.InitializeForEdit(new WorkEntryBuilder().WithId(7).WithTimes(9, 10).Build());

		await vm.SaveCommand.ExecuteAsync(null);

		_orchestrator.Verify(o => o.SaveExistingAsync(
			7, It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
		vm.DialogResult.Should().BeTrue();
	}

	[Fact]
	public async Task Save_Failure_ShowsErrorAndKeepsDialogOpen()
	{
		_orchestrator
			.Setup(o => o.SaveNewAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<bool>("overlap"));
		var vm = CreateViewModel();
		var closed = false;
		vm.CloseAction = () => closed = true;
		vm.InitializeForNew(ticketId: "PROJ-1");

		await vm.SaveCommand.ExecuteAsync(null);

		vm.ValidationError.Should().Be("overlap");
		vm.DialogResult.Should().BeFalse();
		closed.Should().BeFalse();
	}

	[Fact]
	public async Task Save_UserDeclinedOverlapResolution_KeepsDialogOpenWithoutError()
	{
		_orchestrator
			.Setup(o => o.SaveNewAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(false));
		var vm = CreateViewModel();
		var closed = false;
		vm.CloseAction = () => closed = true;
		vm.InitializeForNew(ticketId: "PROJ-1");

		await vm.SaveCommand.ExecuteAsync(null);

		vm.DialogResult.Should().BeFalse();
		closed.Should().BeFalse();
	}

	[Fact]
	public void Cancel_ClosesWithoutResult()
	{
		var vm = CreateViewModel();
		var closed = false;
		vm.CloseAction = () => closed = true;
		vm.InitializeForNew();

		vm.CancelCommand.Execute(null);

		vm.DialogResult.Should().BeFalse();
		closed.Should().BeTrue();
	}
}
