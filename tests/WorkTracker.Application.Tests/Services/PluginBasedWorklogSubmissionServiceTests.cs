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

	#region SubmitDailyWorklogAsync (no provider)

	[Fact]
	public async Task SubmitDailyWorklogAsync_NoPluginAvailable_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([]);

		var result = await _sut.SubmitDailyWorklogAsync(DateTime.Today, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("No worklog upload plugin available");
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_WithEntries_DelegatesToPlugin()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitDailyWorklogAsync(date, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.SuccessfulEntries.Should().Be(1);
		plugin.Verify(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_NoCompletedEntries_ReturnsSuccessWithZero()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[] { CreateActiveEntry(1, "PROJ-1", date.AddHours(9)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = await _sut.SubmitDailyWorklogAsync(date, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(0);
	}

	#endregion

	#region SubmitDailyWorklogAsync (with provider)

	[Fact]
	public async Task SubmitDailyWorklogAsync_PluginNotFound_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(NonExistentPluginId)).Returns((IWorklogUploadPlugin?)null);

		var result = await _sut.SubmitDailyWorklogAsync(DateTime.Today, NonExistentPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("Plugin 'unknown' not found");
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_AllEntriesValid_UploadsAll()
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
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 2, SuccessfulEntries = 2 }));

		var result = await _sut.SubmitDailyWorklogAsync(date, TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(2);
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_SomeEntriesInvalid_SkipsInvalid()
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
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		// First valid, second invalid
		_mockValidator
			.SetupSequence(v => v.Validate(It.IsAny<WorklogDto>()))
			.Returns(ValidationResult.Success())
			.Returns(ValidationResult.Failure("Invalid"));

		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitDailyWorklogAsync(date, TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(1);
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_AllEntriesInvalid_ReturnsFailure()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorInvalid();

		var result = await _sut.SubmitDailyWorklogAsync(date, TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("No valid worklogs to submit");
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_PluginReturnsFailure_PropagatesError()
	{
		var date = new DateTime(2025, 1, 15);
		var plugin = CreateMockPlugin();
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);

		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Failure("API error"));

		var result = await _sut.SubmitDailyWorklogAsync(date, TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("API error");
	}

	[Fact]
	public async Task SubmitDailyWorklogAsync_ActiveEntriesFilteredOut()
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
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();
		plugin.Setup(p => p.UploadWorklogsAsync(
				It.Is<IEnumerable<PluginWorklogEntry>>(w => w.Count() == 1),
				It.IsAny<WorklogSubmissionMode>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult { TotalEntries = 1, SuccessfulEntries = 1 }));

		var result = await _sut.SubmitDailyWorklogAsync(date, TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.TotalEntries.Should().Be(1);
	}

	#endregion

	#region SubmitWeeklyWorklogAsync

	[Fact]
	public async Task SubmitWeeklyWorklogAsync_NoPlugin_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([]);

		var result = await _sut.SubmitWeeklyWorklogAsync(DateTime.Today, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("No worklog upload plugin available");
	}

	[Fact]
	public async Task SubmitWeeklyWorklogAsync_PluginNotFound_ReturnsFailure()
	{
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(NonExistentPluginId)).Returns((IWorklogUploadPlugin?)null);

		var result = await _sut.SubmitWeeklyWorklogAsync(DateTime.Today, NonExistentPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain("Plugin 'unknown' not found");
	}

	[Fact]
	public async Task SubmitWeeklyWorklogAsync_UsesDateRangeService()
	{
		var date = new DateTime(2025, 1, 15); // Wednesday
		var weekStart = new DateTime(2025, 1, 13); // Monday
		var weekEnd = new DateTime(2025, 1, 19); // Sunday
		var plugin = CreateMockPlugin();

		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(p => p.GetPlugin<IWorklogUploadPlugin>(TestPluginId)).Returns(plugin.Object);
		_mockDateRangeService.Setup(s => s.GetWeekRange(date)).Returns((weekStart, weekEnd));

		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Array.Empty<WorkEntry>());

		var result = await _sut.SubmitWeeklyWorklogAsync(date, TestContext.Current.CancellationToken);

		_mockDateRangeService.Verify(s => s.GetWeekRange(date), Times.Once);
		_mockWorkEntryService.Verify(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()), Times.Once);
	}

	#endregion

	#region PreviewDailyWorklogAsync

	[Fact]
	public async Task PreviewDailyWorklogAsync_ReturnsOnlyCompletedEntries()
	{
		var date = new DateTime(2025, 1, 15);
		var entries = new[]
		{
			CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(10)),
			CreateActiveEntry(2, "PROJ-2", date.AddHours(10))
		};
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = await _sut.PreviewDailyWorklogAsync(date, TestContext.Current.CancellationToken);

		result.Worklogs.Should().HaveCount(1);
		result.Worklogs[0].TicketId.Should().Be("PROJ-1");
	}

	[Fact]
	public async Task PreviewDailyWorklogAsync_EmptyDay_ReturnsEmptyList()
	{
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Array.Empty<WorkEntry>());

		var result = await _sut.PreviewDailyWorklogAsync(DateTime.Today, TestContext.Current.CancellationToken);

		result.Worklogs.Should().BeEmpty();
	}

	[Fact]
	public async Task PreviewDailyWorklogAsync_DurationMinutesCalculatedCorrectly()
	{
		var date = new DateTime(2025, 1, 15);
		// 30 seconds → should be Math.Ceiling(0.5) = 1, Math.Max(1, 1) = 1
		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(9).AddSeconds(30)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = await _sut.PreviewDailyWorklogAsync(date, TestContext.Current.CancellationToken);

		result.Worklogs[0].DurationMinutes.Should().Be(1);
	}

	[Fact]
	public async Task PreviewDailyWorklogAsync_DurationRoundedUp()
	{
		var date = new DateTime(2025, 1, 15);
		// 61 minutes → Math.Ceiling(61) = 61
		var entries = new[] { CreateCompletedEntry(1, "PROJ-1", date.AddHours(9), date.AddHours(9).AddMinutes(61)) };
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		var result = await _sut.PreviewDailyWorklogAsync(date, TestContext.Current.CancellationToken);

		result.Worklogs[0].DurationMinutes.Should().Be(61);
	}

	#endregion

	#region PreviewWeeklyWorklogAsync

	[Fact]
	public async Task PreviewWeeklyWorklogAsync_ReturnsAllDaysOfWeek()
	{
		var date = new DateTime(2025, 1, 15);
		var weekStart = new DateTime(2025, 1, 13);
		var weekEnd = new DateTime(2025, 1, 19);

		_mockDateRangeService.Setup(s => s.GetWeekRange(date)).Returns((weekStart, weekEnd));
		_mockWorkEntryService
			.Setup(s => s.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Array.Empty<WorkEntry>());

		var result = await _sut.PreviewWeeklyWorklogAsync(date, TestContext.Current.CancellationToken);

		result.Should().HaveCount(7);
		result.Keys.Should().Contain(weekStart);
		result.Keys.Should().Contain(weekEnd);
	}

	[Fact]
	public async Task PreviewWeeklyWorklogAsync_GroupsEntriesByDate()
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

		var result = await _sut.PreviewWeeklyWorklogAsync(weekStart, TestContext.Current.CancellationToken);

		result[weekStart].Worklogs.Should().HaveCount(1);
		result[weekStart.AddDays(1)].Worklogs.Should().HaveCount(1);
		result[weekStart.AddDays(2)].Worklogs.Should().BeEmpty();
	}

	#endregion

	#region GetAvailableProviders

	[Fact]
	public void GetAvailableProviders_ReturnsEnabledPlugins()
	{
		var plugin = CreateMockPlugin("tempo", "Tempo");
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([plugin.Object]);

		var providers = _sut.GetAvailableProviders().ToList();

		providers.Should().HaveCount(1);
		providers[0].Id.Should().Be("tempo");
		providers[0].Name.Should().Be("Tempo");
	}

	[Fact]
	public void GetAvailableProviders_NoPlugins_ReturnsEmpty()
	{
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([]);

		_sut.GetAvailableProviders().Should().BeEmpty();
	}

	[Fact]
	public void GetAvailableProviders_ExposesPluginSupportedModes()
	{
		var timedOnly = CreateMockPlugin("gorang3", "GoranG3", WorklogSubmissionMode.Timed);
		var bothModes = CreateMockPlugin("tempo", "Tempo", WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated);
		_mockPluginManager.Setup(p => p.WorklogUploadPlugins).Returns([timedOnly.Object, bothModes.Object]);

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
	public async Task SubmitDailyWorklogAsync_PluginReturnsPartialFailure_MapsErrors()
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
			.Setup(s => s.GetWorkEntriesByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		SetupValidatorValid();

		var failedWorklog = new PluginWorklogEntry { TicketId = "PROJ-2", StartTime = date.AddHours(10), EndTime = date.AddHours(12) };
		plugin.Setup(p => p.UploadWorklogsAsync(It.IsAny<IEnumerable<PluginWorklogEntry>>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<WorklogSubmissionResult>.Success(new WorklogSubmissionResult
			{
				TotalEntries = 2,
				SuccessfulEntries = 1,
				FailedEntries = 1,
				Errors = [new WorklogSubmissionError { Worklog = failedWorklog, ErrorMessage = "Ticket not found" }]
			}));

		var result = await _sut.SubmitDailyWorklogAsync(date, TestPluginId, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value!.Errors.Should().HaveCount(1);
		result.Value.Errors[0].TicketId.Should().Be("PROJ-2");
		result.Value.Errors[0].ErrorMessage.Should().Be("Ticket not found");
	}

	#endregion
}
