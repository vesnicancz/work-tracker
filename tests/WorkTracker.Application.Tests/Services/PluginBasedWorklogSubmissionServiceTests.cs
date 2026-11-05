using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Tests.Services;

public class PluginBasedWorklogSubmissionServiceTests
{
	private const string TestPluginId = "test.plugin";
	private const string NonExistentPluginId = "unknown";

	private readonly Mock<IWorkEntryService> _mockWorkEntryService = new();
	private readonly Mock<IDateRangeService> _mockDateRangeService = new();
	private readonly Mock<IWorklogValidator> _mockValidator = new();
	private readonly Mock<IPluginManager> _mockPluginManager = new();
	private readonly PluginBasedWorklogSubmissionService _sut;

	public PluginBasedWorklogSubmissionServiceTests()
	{
		_sut = new PluginBasedWorklogSubmissionService(
			_mockWorkEntryService.Object,
			_mockDateRangeService.Object,
			_mockValidator.Object,
			_mockPluginManager.Object,
			new Mock<ILogger<PluginBasedWorklogSubmissionService>>().Object);

		SetupValidatorValid();
	}

	private static WorkEntry CreateCompletedEntry(int id, string ticketId, DateTime start, DateTime end) =>
		WorkEntry.Reconstitute(id, ticketId, start, end, "desc", false, start);

	private static WorkEntry CreateActiveEntry(int id, string ticketId, DateTime start) =>
		WorkEntry.Reconstitute(id, ticketId, start, null, "desc", true, start);

	private Mock<IWorklogUploadPlugin> CreateMockPlugin(
		string id = TestPluginId,
		string name = "Test Plugin",
		WorklogSubmissionMode supportedModes = WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated)
	{
		var plugin = new Mock<IWorklogUploadPlugin>();
		plugin.Setup(p => p.Metadata).Returns(new PluginMetadata { Id = id, Name = name, Version = new Version(1, 0), Author = "Test" });
		plugin.Setup(p => p.SupportedModes).Returns(supportedModes);
		return plugin;
	}

	private void SetupValidatorValid()
	{
		_mockValidator
			.Setup(v => v.Validate(It.IsAny<WorklogDto>()))
			.Returns(ValidationResult.Success());
	}

	private void SetupValidatorInvalid()
	{
		_mockValidator
			.Setup(v => v.Validate(It.IsAny<WorklogDto>()))
			.Returns(ValidationResult.Failure("Invalid"));
	}

	#region Submit a day without a provider

	[Fact]
	public async Task SubmitDay_NoPluginAvailable_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([]);

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(DateTime.Today), null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("No worklog upload plugin available");
	}

	[Fact]
	public async Task SubmitWithoutProvider_SeveralPluginsEnabled_FailsWithoutUploading()
	{
		var tempo = CreateMockPlugin("tempo.worklog");
		var goran = CreateMockPlugin("gorang3.worklog");
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([tempo.Object, goran.Object]);

		var daily = await _sut.SubmitAsync(SubmissionPeriod.Day(DateTime.Today), null, TestContext.Current.CancellationToken);
		var weekly = await _sut.SubmitAsync(SubmissionPeriod.WeekOf(DateTime.Today), null, TestContext.Current.CancellationToken);

		daily.IsFailure.Should().BeTrue();
		daily.Error.Should().Contain("tempo.worklog").And.Contain("gorang3.worklog");
		weekly.IsFailure.Should().BeTrue();
		tempo.Verify(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()), Times.Never);
		goran.Verify(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task SubmitDay_WithEntries_DelegatesToPlugin()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.SuccessfulEntries.Should().Be(1);
		plugin.Verify(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SubmitDay_NoCompletedEntries_ReturnsSuccessWithZero()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[] { CreateActiveEntry(1, "PROJ-1", date.AddHours(9)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(0);
	}

	#endregion

	#region Submit a day with a provider

	[Fact]
	public async Task SubmitDay_PluginNotFound_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(NonExistentPluginId)).Returns((IWorklogUploadPlugin?)null);

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(DateTime.Today), NonExistentPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("Plugin 'unknown' not found");
	}

	[Fact]
	public async Task SubmitDay_AllEntriesValid_UploadsAll()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)),
			CreateCompletedEntry(2, "PROJ-2", date.AddHours(10), date.AddHours(12))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 2, SuccessfulEntries = 2 }));

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(2);
	}

	[Fact]
	public async Task SubmitDay_SomeEntriesInvalid_SkipsInvalid()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)),
			CreateCompletedEntry(2, "PROJ-2", date.AddHours(10), date.AddHours(12))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		// First valid, second invalid
		_mockValidator
			.SetupSequence(v => v.Validate(It.IsAny<WorklogDto>()))
			.Returns(ValidationResult.Success())
			.Returns(ValidationResult.Failure("Invalid"));

		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(2);
		result.Value.SuccessfulEntries.Should().Be(1);
		result.Value.FailedEntries.Should().Be(1);
		var error = result.Value.Errors.Should().ContainSingle().Subject;
		error.Date.Should().Be(date);
		error.ErrorMessage.Should().Be("Invalid");
		error.WorklogIndex.Should().Be(1);
	}

	[Fact]
	public async Task SubmitCustomWorklogsAsync_PluginErrorAfterARejectedRow_PointsAtTheSubmittedPosition()
	{
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);
		_mockValidator
			.Setup(v => v.Validate(It.Is<WorklogDto>(w => w.TicketId == "BAD-1")))
			.Returns(ValidationResult.Failure("Invalid"));
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.ReturnsAsync((IEnumerable<PluginWorklogEntry> entries, WorklogSubmissionMode _, CancellationToken _) =>
			{
				var uploaded = entries.ToList();
				return PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
				{
					TotalEntries = uploaded.Count,
					SuccessfulEntries = 1,
					FailedEntries = 1,
					Errors = [new WorklogSubmissionError { WorklogIndex = 1, ErrorMessage = "rejected by server" }]
				});
			});
		var start = new DateTime(2025, 1, 15, 9, 0, 0);
		WorklogDto[] worklogs =
		[
			new() { TicketId = "SAME-1", StartTime = start, EndTime = start.AddHours(1), DurationMinutes = 60 },
			new() { TicketId = "BAD-1", StartTime = start, EndTime = start.AddHours(1), DurationMinutes = 0 },
			new() { TicketId = "SAME-1", StartTime = start, EndTime = start.AddHours(1), DurationMinutes = 60 }
		];

		var result = await _sut.SubmitCustomWorklogsAsync(worklogs, TestPluginId, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.Value!.Errors.Single(e => e.ErrorMessage == "rejected by server").WorklogIndex.Should().Be(2);
		result.Value.Errors.Single(e => e.ErrorMessage == "Invalid").WorklogIndex.Should().Be(1);
	}

	[Fact]
	public async Task SubmitCustomWorklogsAsync_PluginReportsAPositionOutsideTheList_HasNoPosition()
	{
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
			{
				TotalEntries = 1,
				FailedEntries = 1,
				Errors = [new WorklogSubmissionError { WorklogIndex = 5, ErrorMessage = "failed" }]
			}));
		var start = new DateTime(2025, 1, 15, 9, 0, 0);
		WorklogDto[] worklogs = [new() { TicketId = "OK-1", StartTime = start, EndTime = start.AddHours(1), DurationMinutes = 60 }];

		var result = await _sut.SubmitCustomWorklogsAsync(worklogs, TestPluginId, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		var error = result.Value!.Errors.Should().ContainSingle().Subject;
		error.WorklogIndex.Should().BeNull();
		error.Date.Should().BeNull();
		error.ErrorMessage.Should().Be("failed");
	}

	[Fact]
	public async Task SubmitCustomWorklogsAsync_SomeInvalid_UploadsOnlyTheValidAndReportsTheRest()
	{
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);
		_mockValidator
			.Setup(v => v.Validate(It.Is<WorklogDto>(w => w.TicketId == "BAD-1")))
			.Returns(ValidationResult.Failure("Duration must be greater than 0"));
		IEnumerable<PluginWorklogEntry>? uploaded = null;
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.Callback<IEnumerable<PluginWorklogEntry>, WorklogSubmissionMode, CancellationToken>((worklogs, _, _) => uploaded = worklogs.ToList())
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));
		var start = new DateTime(2025, 1, 15, 9, 0, 0);
		WorklogDto[] worklogs =
		[
			new() { TicketId = "OK-1", StartTime = start, EndTime = start.AddHours(1), DurationMinutes = 60 },
			new() { TicketId = "BAD-1", StartTime = start.AddHours(1), EndTime = start.AddHours(2), DurationMinutes = 0 }
		];

		var result = await _sut.SubmitCustomWorklogsAsync(worklogs, TestPluginId, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		uploaded.Should().ContainSingle().Which.TicketId.Should().Be("OK-1");
		result.Value!.SuccessfulEntries.Should().Be(1);
		result.Value.FailedEntries.Should().Be(1);
		result.Value.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("greater than 0");
	}

	[Fact]
	public async Task SubmitDay_AllEntriesInvalid_ReportsThemWithoutCallingThePlugin()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorInvalid();

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.SuccessfulEntries.Should().Be(0);
		result.Value.FailedEntries.Should().Be(1);
		result.Value.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("Invalid");
		plugin.Verify(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task SubmitDay_PluginReturnsFailure_PropagatesError()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Failure("API error"));

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("API error");
	}

	[Fact]
	public async Task SubmitDay_ActiveEntriesFilteredOut()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)),
			CreateActiveEntry(2, "PROJ-2", date.AddHours(10))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(
				It.Is<IEnumerable<PluginWorklogEntry>>(w => w.Count() == 1),
				It.IsAny<WorklogSubmissionMode>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(1);
	}

	#endregion

	#region Submit a week

	[Fact]
	public async Task SubmitWeek_NoPlugin_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([]);

		var result = await _sut.SubmitAsync(SubmissionPeriod.WeekOf(DateTime.Today), null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("No worklog upload plugin available");
	}

	[Fact]
	public async Task SubmitWeek_PluginNotFound_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(NonExistentPluginId)).Returns((IWorklogUploadPlugin?)null);

		var result = await _sut.SubmitAsync(SubmissionPeriod.WeekOf(DateTime.Today), NonExistentPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("Plugin 'unknown' not found");
	}

	[Fact]
	public async Task SubmitWeek_UsesDateRangeService()
	{
		var date = new DateTime(2025, 1, 15); // Wednesday
		var weekStart = new DateTime(2025, 1, 13); // Monday
		var weekEnd = new DateTime(2025, 1, 19); // Sunday
		var plugin = CreateMockPlugin();

		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);
		_mockDateRangeService.Setup(s => s.GetWeekRange(date)).Returns((weekStart, weekEnd));

		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Array.Empty<WorkEntry>());

		var result = await _sut.SubmitAsync(SubmissionPeriod.WeekOf(date), null, TestContext.Current.CancellationToken);

		_mockDateRangeService.Verify(s => s.GetWeekRange(date), Times.Once);
		_mockWorkEntryService.Verify(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()), Times.Once);
	}

	#endregion

	#region Preview of a day

	[Fact]
	public async Task PreviewDay_ReturnsOnlyCompletedEntries()
	{
		var date = new DateTime(2025, 1, 15);
		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)),
			CreateActiveEntry(2, "PROJ-2", date.AddHours(10))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = (await _sut.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken)).Single();

		result.Worklogs.Should().HaveCount(1);
		result.Worklogs[0].TicketId.Should().Be("PROJ-1");
	}

	[Fact]
	public async Task PreviewDay_EmptyDay_ReturnsEmptyList()
	{
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Array.Empty<WorkEntry>());

		var result = (await _sut.PreviewAsync(SubmissionPeriod.Day(DateTime.Today), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken)).Single();

		result.Worklogs.Should().BeEmpty();
	}

	[Fact]
	public async Task PreviewDay_DurationMinutesCalculatedCorrectly()
	{
		var date = new DateTime(2025, 1, 15);
		// 30 seconds → should be Math.Ceiling(0.5) = 1, Math.Max(1, 1) = 1
		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(9).AddSeconds(30)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = (await _sut.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken)).Single();

		result.Worklogs[0].DurationMinutes.Should().Be(1);
	}

	[Fact]
	public async Task PreviewDay_DurationRoundedUp()
	{
		var date = new DateTime(2025, 1, 15);
		// 61 minutes → Math.Ceiling(61) = 61
		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(9).AddMinutes(61)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = (await _sut.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken)).Single();

		result.Worklogs[0].DurationMinutes.Should().Be(61);
	}

	#endregion

	#region Preview of a week

	[Fact]
	public async Task PreviewWeek_ReturnsAllDaysOfWeekInOrder()
	{
		var date = new DateTime(2025, 1, 15);
		var weekStart = new DateTime(2025, 1, 13);
		var weekEnd = new DateTime(2025, 1, 19);

		_mockDateRangeService.Setup(s => s.GetWeekRange(date)).Returns((weekStart, weekEnd));
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Array.Empty<WorkEntry>());

		var result = await _sut.PreviewAsync(SubmissionPeriod.WeekOf(date), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.Select(day => day.SubmissionDate).Should().Equal(Enumerable.Range(0, 7).Select(offset => weekStart.AddDays(offset)));
	}

	[Fact]
	public async Task PreviewWeek_GroupsEntriesByDate()
	{
		var weekStart = new DateTime(2025, 1, 13);
		var weekEnd = new DateTime(2025, 1, 19);

		_mockDateRangeService.Setup(s => s.GetWeekRange(It.IsAny<DateTime>())).Returns((weekStart, weekEnd));

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", weekStart.AddHours(9), weekStart.AddHours(10)),
			CreateCompletedEntry(2, "PROJ-2", weekStart.AddDays(1).AddHours(9), weekStart.AddDays(1).AddHours(10))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = await _sut.PreviewAsync(SubmissionPeriod.WeekOf(weekStart), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result[0].Worklogs.Should().HaveCount(1);
		result[1].Worklogs.Should().HaveCount(1);
		result[2].Worklogs.Should().BeEmpty();
	}

	#endregion

	#region Aggregated preview

	private void SetupDay(DateTime date, params WorkEntry[] entries)
	{
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);
	}

	private static WorkEntry Entry(int id, string? ticket, string? description, DateTime start, int minutes) =>
		WorkEntry.Reconstitute(id, ticket, start, start.AddMinutes(minutes), description, false, start);

	[Fact]
	public async Task PreviewAggregated_GroupsByTicketAndDescription()
	{
		var date = new DateTime(2025, 1, 15);
		SetupDay(date,
			// Same ticket and description become one row
			Entry(1, "PROJ-1", "Work", date.AddHours(9), 60),
			Entry(2, "PROJ-1", "Review", date.AddHours(11), 45),
			Entry(3, "PROJ-2", "Other", date.AddHours(12), 15),
			Entry(4, "PROJ-1", "Work", date.AddHours(14), 30));

		var day = (await _sut.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Aggregated, TestContext.Current.CancellationToken)).Single();

		day.Worklogs.Select(w => (w.TicketId, w.Description, w.DurationMinutes)).Should().Equal(
			("PROJ-1", "Work", 90),
			("PROJ-1", "Review", 45),
			("PROJ-2", "Other", 15));
		day.Worklogs[0].StartTime.Should().Be(date.AddHours(9));
		day.Worklogs[0].EndTime.Should().Be(date.AddHours(9), "an aggregated row has no real interval");
	}

	[Fact]
	public async Task PreviewAggregated_NoTicket_GroupsIntoOneRowWithoutATicket()
	{
		var date = new DateTime(2025, 1, 15);
		SetupDay(date,
			Entry(1, null, "Admin", date.AddHours(9), 20),
			Entry(2, null, "Admin", date.AddHours(16), 10));

		var day = (await _sut.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Aggregated, TestContext.Current.CancellationToken)).Single();

		var row = day.Worklogs.Should().ContainSingle().Subject;
		row.TicketId.Should().BeNull();
		row.DurationMinutes.Should().Be(30);
	}

	[Fact]
	public async Task PreviewAggregated_Week_KeepsDaysApart()
	{
		var monday = new DateTime(2025, 1, 13);
		var sunday = new DateTime(2025, 1, 19);
		_mockDateRangeService.Setup(s => s.GetWeekRange(monday)).Returns((monday, sunday));
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(monday, sunday, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new[]
			{
				Entry(1, "PROJ-1", "Work", monday.AddHours(9), 60),
				Entry(2, "PROJ-1", "Work", monday.AddHours(14), 30),
				Entry(3, "PROJ-1", "Work", monday.AddDays(1).AddHours(9), 45)
			});

		var days = await _sut.PreviewAsync(SubmissionPeriod.WeekOf(monday), WorklogSubmissionMode.Aggregated, TestContext.Current.CancellationToken);

		days[0].Worklogs.Should().ContainSingle().Which.DurationMinutes.Should().Be(90);
		days[1].Worklogs.Should().ContainSingle().Which.DurationMinutes.Should().Be(45);
	}

	[Fact]
	public async Task PreviewTimed_KeepsEveryEntry()
	{
		var date = new DateTime(2025, 1, 15);
		SetupDay(date,
			Entry(1, "PROJ-1", "Work", date.AddHours(9), 60),
			Entry(2, "PROJ-1", "Work", date.AddHours(14), 30));

		var day = (await _sut.PreviewAsync(SubmissionPeriod.Day(date), WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken)).Single();

		day.Worklogs.Should().HaveCount(2);
	}

	#endregion

	#region GetAvailableProviders

	[Fact]
	public void GetAvailableProviders_ReturnsEnabledPlugins()
	{
		var plugin = CreateMockPlugin("tempo", "Tempo");
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([plugin.Object]);

		var providers = _sut.GetAvailableProviders().ToList();

		providers.Should().HaveCount(1);
		providers[0].Id.Should().Be("tempo");
		providers[0].Name.Should().Be("Tempo");
	}

	[Fact]
	public void GetAvailableProviders_NoPlugins_ReturnsEmpty()
	{
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([]);

		_sut.GetAvailableProviders().Should().BeEmpty();
	}

	[Fact]
	public void GetAvailableProviders_ExposesPluginSupportedModes()
	{
		var timedOnly = CreateMockPlugin("gorang3", "GoranG3", WorklogSubmissionMode.Timed);
		var bothModes = CreateMockPlugin("tempo", "Tempo", WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated);
		_mockPluginManager.Setup(p => p.EnabledWorklogUploadPlugins).Returns([timedOnly.Object, bothModes.Object]);

		var providers = _sut.GetAvailableProviders().ToList();

		providers.Should().HaveCount(2);
		providers.Single(p => p.Id == "gorang3").SupportedModes.Should().Be(WorklogSubmissionMode.Timed);
		providers.Single(p => p.Id == "tempo").SupportedModes.Should().Be(WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated);
	}

	#endregion

	#region SubmitCustomWorklogsAsync

	[Fact]
	public async Task SubmitCustomWorklogsAsync_PluginNotFound_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(NonExistentPluginId)).Returns((IWorklogUploadPlugin?)null);

		var result = await _sut.SubmitCustomWorklogsAsync([], NonExistentPluginId, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("Plugin 'unknown' not found");
	}

	[Fact]
	public async Task SubmitCustomWorklogsAsync_ValidWorklogs_UploadsSuccessfully()
	{
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var worklogs = new List<WorklogDto>
		{
			new() { TicketId = "PROJ-1", StartTime = DateTime.Today.AddHours(9), EndTime = DateTime.Today.AddHours(10), DurationMinutes = 60 }
		};

		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitCustomWorklogsAsync(worklogs, TestPluginId, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.SuccessfulEntries.Should().Be(1);
	}

	[Fact]
	public async Task SubmitCustomWorklogsAsync_PluginFailure_PropagatesError()
	{
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var worklogs = new List<WorklogDto>
		{
			new() { TicketId = "PROJ-1", StartTime = DateTime.Today.AddHours(9), EndTime = DateTime.Today.AddHours(10), DurationMinutes = 60 }
		};

		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Failure("Connection refused"));

		var result = await _sut.SubmitCustomWorklogsAsync(worklogs, TestPluginId, WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("Connection refused");
	}

	[Fact]
	public async Task SubmitCustomWorklogsAsync_UnsupportedMode_ReturnsFailureBeforeCallingPlugin()
	{
		var plugin = CreateMockPlugin(supportedModes: WorklogSubmissionMode.Timed);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var worklogs = new List<WorklogDto>
		{
			new() { TicketId = "PROJ-1", StartTime = DateTime.Today.AddHours(9), EndTime = DateTime.Today.AddHours(10), DurationMinutes = 60 }
		};

		var result = await _sut.SubmitCustomWorklogsAsync(worklogs, TestPluginId, WorklogSubmissionMode.Aggregated, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("does not support submission mode");
		plugin.Verify(
			p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	#endregion

	#region Error mapping

	[Fact]
	public async Task SubmitDay_PluginReturnsPartialFailure_MapsErrors()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)),
			CreateCompletedEntry(2, "PROJ-2", date.AddHours(10), date.AddHours(12))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(date, date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();

		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
			{
				TotalEntries = 2,
				SuccessfulEntries = 1,
				FailedEntries = 1,
				Errors = [new WorklogSubmissionError { WorklogIndex = 1, ErrorMessage = "Ticket not found" }]
			}));

		var result = await _sut.SubmitAsync(SubmissionPeriod.Day(date), TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.Errors.Should().HaveCount(1);
		result.Value.Errors[0].WorklogIndex.Should().Be(1);
		result.Value.Errors[0].Date.Should().Be(date);
		result.Value.Errors[0].ErrorMessage.Should().Be("Ticket not found");
	}

	#endregion
}
