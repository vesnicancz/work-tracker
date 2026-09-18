using System.Text.Json;
using FluentAssertions;
using Moq;
using Spectre.Console;
using Spectre.Console.Testing;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.CLI.Commands;
using WorkTracker.CLI.Output;
using WorkTracker.Domain.Entities;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Builders;

namespace WorkTracker.CLI.Tests.Commands;

public sealed class CommandHandlerTests : IDisposable
{
	private static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	private readonly Mock<IWorkEntryService> _workEntryService = new();
	private readonly Mock<IWorklogSubmissionService> _submissionService = new();
	private readonly Mock<IPluginManager> _pluginManager = new();
	private readonly TestConsole _console = new();
	private readonly TestConsole _errorConsole = new();
	private readonly StringWriter _data = new();
	private readonly CommandHandler _handler;

	public CommandHandlerTests()
	{
		AnsiConsole.Console = _console;
		CliConsole.Error = _errorConsole;
		CliConsole.Data = _data;

		var timeProvider = new Mock<TimeProvider>();
		timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(LocalNow, TimeSpan.Zero));
		timeProvider.SetupGet(t => t.LocalTimeZone).Returns(TimeZoneInfo.Utc);

		_handler = new CommandHandler(
			_workEntryService.Object, _submissionService.Object, _pluginManager.Object, timeProvider.Object);
	}

	/// <summary>Everything the command wrote, whichever stream it chose.</summary>
	private string AllOutput => _console.Output + _errorConsole.Output + _data.ToString();

	public void Dispose()
	{
		CliConsole.Data = Console.Out;
		_data.Dispose();
		_errorConsole.Dispose();
		_console.Dispose();
	}

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
		_errorConsole.Output.Should().Contain("start collision");
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
		_errorConsole.Output.Should().Contain("no active entry");
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

	[Fact]
	public async Task Status_Json_NoActiveEntry_WritesInactiveDocument()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var exitCode = await _handler.HandleStatusCommand(json: true);

		exitCode.Should().Be(0);
		var root = JsonDocument.Parse(_data.ToString()).RootElement;
		root.GetProperty("active").GetBoolean().Should().BeFalse();
		root.GetProperty("entry").ValueKind.Should().Be(JsonValueKind.Null);
		_console.Output.Should().BeEmpty("JSON mode must not mix prose into stdout");
	}

	[Fact]
	public async Task Status_Json_ActiveEntry_WritesEntryAndElapsed()
	{
		_workEntryService.Setup(s => s.GetActiveWorkAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new WorkEntryBuilder()
				.WithId(7)
				.WithTicketId("PROJ-42")
				.WithDescription("Fix")
				.WithStartTime(LocalNow.AddHours(-2))
				.Active()
				.Build());

		var exitCode = await _handler.HandleStatusCommand(json: true);

		exitCode.Should().Be(0);
		var root = JsonDocument.Parse(_data.ToString()).RootElement;
		root.GetProperty("active").GetBoolean().Should().BeTrue();
		root.GetProperty("elapsedMinutes").GetInt32().Should().Be(120);
		root.GetProperty("entry").GetProperty("id").GetInt32().Should().Be(7);
		root.GetProperty("entry").GetProperty("ticketId").GetString().Should().Be("PROJ-42");
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

	[Fact]
	public async Task List_Json_WritesEntriesAndTotal()
	{
		_workEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CompletedEntry(1, 9, 10), CompletedEntry(2, 10, 12)]);

		var exitCode = await _handler.HandleListCommand(LocalNow.Date, json: true);

		exitCode.Should().Be(0);
		var root = JsonDocument.Parse(_data.ToString()).RootElement;
		root.GetProperty("date").GetString().Should().Be("2026-01-15");
		root.GetProperty("totalMinutes").GetInt32().Should().Be(180);
		root.GetProperty("entries").GetArrayLength().Should().Be(2);
		root.GetProperty("entries")[0].GetProperty("durationMinutes").GetInt32().Should().Be(60);
		_console.Output.Should().BeEmpty("JSON mode must not mix prose into stdout");
	}

	[Fact]
	public async Task List_Json_NoEntries_WritesEmptyArrayNotProse()
	{
		_workEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		var exitCode = await _handler.HandleListCommand(json: true);

		exitCode.Should().Be(0);
		var root = JsonDocument.Parse(_data.ToString()).RootElement;
		root.GetProperty("entries").GetArrayLength().Should().Be(0);
		root.GetProperty("totalMinutes").GetInt32().Should().Be(0);
		AllOutput.Should().NotContain("No entries found");
	}

	[Fact]
	public async Task List_Json_EscapesMarkupCharactersInDescription()
	{
		// "[" would be read as a Spectre markup tag on the styled path; raw JSON must keep it verbatim.
		_workEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([new WorkEntryBuilder().WithId(1).WithTicketId("PROJ-1").WithTimes(9, 10).WithDescription("[urgent] fix").Build()]);

		var exitCode = await _handler.HandleListCommand(LocalNow.Date, json: true);

		exitCode.Should().Be(0);
		var root = JsonDocument.Parse(_data.ToString()).RootElement;
		root.GetProperty("entries")[0].GetProperty("description").GetString().Should().Be("[urgent] fix");
	}

	#endregion List

	#region Edit / Delete

	[Fact]
	public async Task Edit_Success_ReturnsZero()
	{
		var existing = CompletedEntry(5);
		_workEntryService
			.Setup(s => s.GetWorkEntryByIdAsync(5, It.IsAny<CancellationToken>()))
			.ReturnsAsync(existing);
		_workEntryService
			.Setup(s => s.UpdateWorkEntryAsync(5, "PROJ-9", existing.StartTime, existing.EndTime, existing.Description, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(CompletedEntry(5)));

		var exitCode = await _handler.HandleEditCommand(5, "PROJ-9");

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("Updated work entry").And.Contain("#5");
	}

	[Fact]
	public async Task Edit_OmittedOptions_KeepTheirStoredValues()
	{
		var existing = new WorkEntryBuilder()
			.WithId(5)
			.WithTicketId("PROJ-9")
			.WithTimes(9, 11)
			.WithDescription("Bug fix")
			.Build();
		_workEntryService
			.Setup(s => s.GetWorkEntryByIdAsync(5, It.IsAny<CancellationToken>()))
			.ReturnsAsync(existing);
		_workEntryService
			.Setup(s => s.UpdateWorkEntryAsync(5, It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(existing));

		// Only the description is edited; ticket and times must survive untouched.
		var exitCode = await _handler.HandleEditCommand(5, description: "Updated description");

		exitCode.Should().Be(0);
		_workEntryService.Verify(
			s => s.UpdateWorkEntryAsync(5, "PROJ-9", existing.StartTime, existing.EndTime, "Updated description", It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task Edit_WithoutAnyOption_ReturnsOneAndDoesNotUpdate()
	{
		var exitCode = await _handler.HandleEditCommand(5);

		exitCode.Should().Be(1);
		_errorConsole.Output.Should().Contain("Nothing to change");
		_workEntryService.Verify(
			s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[Fact]
	public async Task Edit_UnknownId_ReturnsOneAndDoesNotUpdate()
	{
		_workEntryService
			.Setup(s => s.GetWorkEntryByIdAsync(999, It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		var exitCode = await _handler.HandleEditCommand(999, "PROJ-9");

		exitCode.Should().Be(1);
		_errorConsole.Output.Should().Contain("999").And.Contain("not found");
		_workEntryService.Verify(
			s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[Fact]
	public async Task Edit_Failure_ReturnsOne()
	{
		_workEntryService
			.Setup(s => s.GetWorkEntryByIdAsync(999, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CompletedEntry(999));
		_workEntryService
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("entry not found"));

		var exitCode = await _handler.HandleEditCommand(999, "PROJ-9");

		exitCode.Should().Be(1);
		_errorConsole.Output.Should().Contain("entry not found");
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
		_errorConsole.Output.Should().Contain("entry not found");
	}

	#endregion Edit / Delete

	#region Providers

	private static IWorklogUploadPlugin UploadPlugin(string id, string name)
	{
		var plugin = new Mock<IWorklogUploadPlugin>();
		plugin.SetupGet(p => p.Metadata).Returns(new PluginMetadata
		{
			Id = id,
			Name = name,
			Version = new Version(1, 0),
			Author = "test"
		});
		return plugin.Object;
	}

	private void SetupPlugins(IReadOnlyList<IWorklogUploadPlugin> all, IReadOnlyList<IWorklogUploadPlugin> enabled)
	{
		_pluginManager.SetupGet(m => m.AllWorklogUploadPlugins).Returns(all);
		_pluginManager.SetupGet(m => m.WorklogUploadPlugins).Returns(enabled);
	}

	[Fact]
	public void Providers_ListsEnabledAndDisabled()
	{
		var tempo = UploadPlugin("tempo.worklog", "Tempo Timesheets");
		var goran = UploadPlugin("gorang3.worklog", "GoranG3");
		SetupPlugins([tempo, goran], [goran]);

		var exitCode = _handler.HandleProvidersCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("gorang3.worklog").And.Contain("enabled");
		_console.Output.Should().Contain("tempo.worklog").And.Contain("disabled");
	}

	[Fact]
	public void Providers_NoneInstalled_ExplainsWhere()
	{
		SetupPlugins([], []);

		var exitCode = _handler.HandleProvidersCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No worklog upload plugins installed").And.Contain("plugins/");
	}

	[Fact]
	public void Providers_InstalledButNoneEnabled_SaysSo()
	{
		SetupPlugins([UploadPlugin("tempo.worklog", "Tempo Timesheets")], []);

		var exitCode = _handler.HandleProvidersCommand();

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No provider is enabled");
	}

	[Fact]
	public void Providers_Json_ReportsIdNameAndEnabled()
	{
		var tempo = UploadPlugin("tempo.worklog", "Tempo Timesheets");
		SetupPlugins([tempo], [tempo]);

		var exitCode = _handler.HandleProvidersCommand(json: true);

		exitCode.Should().Be(0);
		var root = JsonDocument.Parse(_data.ToString()).RootElement;
		root.GetArrayLength().Should().Be(1);
		root[0].GetProperty("id").GetString().Should().Be("tempo.worklog");
		root[0].GetProperty("name").GetString().Should().Be("Tempo Timesheets");
		root[0].GetProperty("enabled").GetBoolean().Should().BeTrue();
		_console.Output.Should().BeEmpty();
	}

	#endregion Providers

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
	public async Task Send_AssumeYes_SubmitsWithoutPrompting()
	{
		// The console is left non-interactive on purpose: a prompt here would throw, which is
		// exactly what "--yes" has to avoid in a script or scheduled task.
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith(Worklog()));
		_submissionService
			.Setup(s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var exitCode = await _handler.HandleSendCommand(LocalNow.Date, assumeYes: true);

		exitCode.Should().Be(0);
		_console.Output.Should().NotContain("Send these entries");
		_submissionService.Verify(
			s => s.SubmitDailyWorklogAsync(LocalNow.Date, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SendWeek_AssumeYes_SubmitsWithoutPrompting()
	{
		_submissionService
			.Setup(s => s.PreviewWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Dictionary<DateTime, WorklogSubmissionDto> { [LocalNow.Date] = PreviewWith(Worklog()) });
		_submissionService
			.Setup(s => s.SubmitWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var exitCode = await _handler.HandleSendCommand(LocalNow.Date, isWeek: true, assumeYes: true);

		exitCode.Should().Be(0);
		_console.Output.Should().NotContain("Send all these entries");
		_submissionService.Verify(
			s => s.SubmitWeeklyWorklogAsync(LocalNow.Date, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Send_WithProvider_SubmitsThroughThatProvider()
	{
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith(Worklog()));
		_submissionService
			.Setup(s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var exitCode = await _handler.HandleSendCommand(LocalNow.Date, assumeYes: true, providerId: "tempo.worklog");

		exitCode.Should().Be(0);
		_submissionService.Verify(
			s => s.SubmitDailyWorklogAsync(LocalNow.Date, "tempo.worklog", It.IsAny<CancellationToken>()), Times.Once);
		_submissionService.Verify(
			s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task SendWeek_WithProvider_SubmitsThroughThatProvider()
	{
		_submissionService
			.Setup(s => s.PreviewWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new Dictionary<DateTime, WorklogSubmissionDto> { [LocalNow.Date] = PreviewWith(Worklog()) });
		_submissionService
			.Setup(s => s.SubmitWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(new SubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var exitCode = await _handler.HandleSendCommand(LocalNow.Date, isWeek: true, assumeYes: true, providerId: "tempo.worklog");

		exitCode.Should().Be(0);
		_submissionService.Verify(
			s => s.SubmitWeeklyWorklogAsync(LocalNow.Date, "tempo.worklog", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Send_UnknownProvider_ReportsFailureOnStderr()
	{
		_submissionService
			.Setup(s => s.PreviewDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PreviewWith(Worklog()));
		_submissionService
			.Setup(s => s.SubmitDailyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<SubmissionResult>("Plugin 'nope' not found"));

		var exitCode = await _handler.HandleSendCommand(LocalNow.Date, assumeYes: true, providerId: "nope");

		exitCode.Should().Be(1);
		_errorConsole.Output.Should().Contain("not found");
	}

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
		_errorConsole.Output.Should().Contain("Tempo unavailable");
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
	public async Task SendWeek_AllDaysEmpty_ReturnsZeroWithoutPrompting()
	{
		// The real preview holds one (possibly empty) entry per day of the week, never an
		// empty dictionary — a week with nothing in it must not offer to send anything.
		var weekStart = LocalNow.Date.AddDays(-3);
		var emptyWeek = Enumerable.Range(0, 7)
			.ToDictionary(
				offset => weekStart.AddDays(offset),
				offset => new WorklogSubmissionDto { SubmissionDate = weekStart.AddDays(offset), Worklogs = [] });

		_submissionService
			.Setup(s => s.PreviewWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(emptyWeek);

		var exitCode = await _handler.HandleSendCommand(isWeek: true);

		exitCode.Should().Be(0);
		_console.Output.Should().Contain("No completed entries to send for the week");
		_console.Output.Should().NotContain("Send all these entries");
		_submissionService.Verify(
			s => s.SubmitWeeklyWorklogAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
			Times.Never);
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
		_console.Output.Should().Contain("Successfully sent 1 entries");
		_errorConsole.Output.Should().Contain("quota exceeded");
	}

	#endregion Send
}
