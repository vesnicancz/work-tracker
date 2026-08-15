using FluentAssertions;
using Moq;
using Spectre.Console;
using Spectre.Console.Testing;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.CLI.Commands;
using WorkTracker.Domain.Entities;
using WorkTracker.Tests.Common.Builders;

namespace WorkTracker.CLI.Tests.Commands;

public sealed class CommandHandlerTests : IDisposable
{
	private static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	private readonly Mock<IWorkEntryService> _workEntryService = new();
	private readonly Mock<IWorklogSubmissionService> _submissionService = new();
	private readonly TestConsole _console = new();
	private readonly CommandHandler _handler;

	public CommandHandlerTests()
	{
		AnsiConsole.Console = _console;

		var timeProvider = new Mock<TimeProvider>();
		timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(LocalNow, TimeSpan.Zero));
		timeProvider.SetupGet(t => t.LocalTimeZone).Returns(TimeZoneInfo.Utc);

		_handler = new CommandHandler(_workEntryService.Object, _submissionService.Object, timeProvider.Object);
	}

	public void Dispose() => _console.Dispose();

	private static WorkEntry ActiveEntry(int id = 1, string ticket = "PROJ-1") =>
		new WorkEntryBuilder().WithId(id).WithTicketId(ticket).Active().Build();

	private static WorkEntry CompletedEntry(int id = 1, int startHour = 9, int endHour = 10) =>
		new WorkEntryBuilder().WithId(id).WithTimes(startHour, endHour).Build();

	#region Start

	[Fact]
	public async Task Start_Success_ReturnsZeroAndReportsEntry()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);
		_workEntryService
			.Setup(s => s.StartWorkAsync("PROJ-1", null, "Fix", null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(ActiveEntry()));

		var exitCode = await _handler.HandleStartCommand("PROJ-1", null, "Fix");

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Started work on").And.Contain("PROJ-1");
	}

	[Fact]
	public async Task Start_WithActiveEntry_ReportsAutoStop()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(ActiveEntry(ticket: "OLD-7"));
		_workEntryService
			.Setup(s => s.StartWorkAsync(It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(ActiveEntry(ticket: "PROJ-1")));

		var exitCode = await _handler.HandleStartCommand("PROJ-1");

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Auto-stopped previous work").And.Contain("OLD-7");
	}

	[Fact]
	public async Task Start_Failure_ReturnsOneAndPrintsError()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);
		_workEntryService
			.Setup(s => s.StartWorkAsync(It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("start collision"));

		var exitCode = await _handler.HandleStartCommand("PROJ-1");

		exitCode.Should().Be(1);
		_console.Output.Should().Contain("start collision");
	}

	#endregion Start

	#region Stop

	[Fact]
	public async Task Stop_Success_ReportsDuration()
	{
		_workEntryService
			.Setup(s => s.StopWorkAsync(null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(CompletedEntry(startHour: 9, endHour: 11)));

		var exitCode = await _handler.HandleStopCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Stopped work on").And.Contain("2h 0m");
	}

	[Fact]
	public async Task Stop_Failure_ReturnsOne()
	{
		_workEntryService
			.Setup(s => s.StopWorkAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("no active entry"));

		var exitCode = await _handler.HandleStopCommand();

		exitCode.Should().Be(1);
		_console.Output.Should().Contain("no active entry");
	}

	#endregion Stop

	#region Status

	[Fact]
	public async Task Status_NoActiveEntry_ReturnsZero()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var exitCode = await _handler.HandleStatusCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No active work entry");
	}

	[Fact]
	public async Task Status_ActiveEntry_ShowsTicketAndStatus()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(ActiveEntry(ticket: "PROJ-42"));

		var exitCode = await _handler.HandleStatusCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("ACTIVE").And.Contain("PROJ-42");
	}

	#endregion Status

	#region List

	[Fact]
	public async Task List_NoEntries_ReturnsZero()
	{
		_workEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		var exitCode = await _handler.HandleListCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No entries found");
		_workEntryService.Verify(
			s => s.GetWorkEntriesByDateAsync(LocalNow.Date, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task List_WithEntries_ShowsTotal()
	{
		_workEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CompletedEntry(1, 9, 10), CompletedEntry(2, 10, 12)]);

		var exitCode = await _handler.HandleListCommand(LocalNow.Date);

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Total:").And.Contain("3h 0m");
	}

	#endregion List

	#region Edit / Delete

	[Fact]
	public async Task Edit_Success_ReturnsZero()
	{
		_workEntryService
			.Setup(s => s.UpdateWorkEntryAsync(5, "PROJ-9", null, null, null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(CompletedEntry(5)));

		var exitCode = await _handler.HandleEditCommand(5, "PROJ-9");

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Updated work entry").And.Contain("#5");
	}

	[Fact]
	public async Task Edit_Failure_ReturnsOne()
	{
		_workEntryService
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("entry not found"));

		var exitCode = await _handler.HandleEditCommand(999);

		exitCode.Should().Be(1);
		_console.Output.Should().Contain("entry not found");
	}

	[Fact]
	public async Task Delete_Success_ReturnsZero()
	{
		_workEntryService
			.Setup(s => s.DeleteWorkEntryAsync(5, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());

		var exitCode = await _handler.HandleDeleteCommand(5);

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Deleted work entry").And.Contain("#5");
	}

	[Fact]
	public async Task Delete_Failure_ReturnsOne()
	{
		_workEntryService
			.Setup(s => s.DeleteWorkEntryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure("entry not found"));

		var exitCode = await _handler.HandleDeleteCommand(999);

		exitCode.Should().Be(1);
		_console.Output.Should().Contain("entry not found");
	}

	#endregion Edit / Delete

	#region Send

	private static WorklogSubmissionDto PreviewWith(params WorklogDto[] worklogs) =>
		new() { Worklogs = [.. worklogs], SubmissionDate = LocalNow.Date };

	private static WorklogDto Worklog(string ticket = "PROJ-1", int minutes = 60) =>
		new()
		{
			TicketId = ticket,
			StartTime = LocalNow.Date.AddHours(9),
			EndTime = LocalNow.Date.AddHours(9).AddMinutes(minutes),
			DurationMinutes = minutes,
		};

	[Fact]
	public async Task Send_NoWorklogs_ReturnsZeroWithoutSubmitting()
	{
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith());

		var exitCode = await _handler.HandleSendCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No completed entries to send");
		_submissionService.Verify(
			s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task Send_Declined_DoesNotSubmit()
	{
		_console.Interactive();
		_console.Input.PushTextWithEnter("n");
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith(Worklog()));

		var exitCode = await _handler.HandleSendCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Cancelled");
		_submissionService.Verify(
			s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task Send_Confirmed_SubmitsAndReportsCount()
	{
		_console.Interactive();
		_console.Input.PushTextWithEnter("y");
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith(Worklog()));
		_submissionService
			.Setup(s => s.SubmitDailyWorklogAsync(LocalNow.Date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var exitCode = await _handler.HandleSendCommand(LocalNow.Date);

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Successfully sent 1 entries");
	}

	[Fact]
	public async Task Send_SubmissionFails_ReturnsOne()
	{
		_console.Interactive();
		_console.Input.PushTextWithEnter("y");
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith(Worklog()));
		_submissionService
			.Setup(s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<SubmissionResult>("Tempo unavailable"));

		var exitCode = await _handler.HandleSendCommand();

		exitCode.Should().Be(1);
		_console.Output.Should().Contain("Tempo unavailable");
	}

	[Fact]
	public async Task SendWeek_NoWorklogs_ReturnsZero()
	{
		_submissionService
			.Setup(s => s.PreviewWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		var exitCode = await _handler.HandleSendCommand(isWeek: true);

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No completed entries to send for the week");
	}

	[Fact]
	public async Task SendWeek_Confirmed_SubmitsAndReportsPartialFailures()
	{
		_console.Interactive();
		_console.Input.PushTextWithEnter("y");
		_submissionService
			.Setup(s => s.PreviewWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Dictionary<DateTime, WorklogSubmissionDto>
			{
				[LocalNow.Date] = PreviewWith(Worklog()),
			});
		_submissionService
			.Setup(s => s.SubmitWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new SubmissionResult
			{
				TotalEntries = 2,
				SuccessfulEntries = 1,
				FailedEntries = 1,
				Errors = [new SubmissionError { Date = LocalNow.Date, ErrorMessage = "quota exceeded" }],
			}));

		var exitCode = await _handler.HandleSendCommand(isWeek: true);

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Successfully sent 1 entries").And.Contain("quota exceeded");
	}

	#endregion Send
}
