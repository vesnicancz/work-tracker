using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class WorkEntryEditOrchestratorTests
{
	private readonly Mock<IWorklogStateService> _mockStateService;
	private readonly Mock<ILocalizationService> _mockLocalization;
	private readonly WorkEntryEditOrchestrator _orchestrator;

	public WorkEntryEditOrchestratorTests()
	{
		_mockStateService = new Mock<IWorklogStateService>();
		_mockLocalization = new Mock<ILocalizationService>();
		_mockLocalization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_mockLocalization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);

		_orchestrator = new WorkEntryEditOrchestrator(
			_mockStateService.Object,
			_mockLocalization.Object,
			new Mock<ILogger<WorkEntryEditOrchestrator>>().Object);
	}

	[Fact]
	public void Validate_BothTicketAndDescription_ReturnsNull()
	{
		_orchestrator.Validate("PROJ-1", "desc", false, DateTime.Now, null)
			.Should().BeNull();
	}

	[Fact]
	public void Validate_TicketOnly_ReturnsNull()
	{
		_orchestrator.Validate("PROJ-1", null, false, DateTime.Now, null)
			.Should().BeNull();
	}

	[Fact]
	public void Validate_DescriptionOnly_ReturnsNull()
	{
		_orchestrator.Validate(null, "description", false, DateTime.Now, null)
			.Should().BeNull();
	}

	[Fact]
	public void Validate_BothEmpty_ReturnsError()
	{
		_orchestrator.Validate(null, null, false, DateTime.Now, null)
			.Should().NotBeNull();
	}

	[Fact]
	public void Validate_BothWhitespace_ReturnsError()
	{
		_orchestrator.Validate("  ", "  ", false, DateTime.Now, null)
			.Should().NotBeNull();
	}

	[Fact]
	public void Validate_EndBeforeStart_ReturnsError()
	{
		var start = new DateTime(2025, 1, 1, 10, 0, 0);
		var end = new DateTime(2025, 1, 1, 9, 0, 0);

		_orchestrator.Validate("PROJ-1", null, true, start, end)
			.Should().NotBeNull();
	}

	[Fact]
	public void Validate_EndAfterStart_ReturnsNull()
	{
		var start = new DateTime(2025, 1, 1, 9, 0, 0);
		var end = new DateTime(2025, 1, 1, 10, 0, 0);

		_orchestrator.Validate("PROJ-1", null, true, start, end)
			.Should().BeNull();
	}

	[Fact]
	public void Validate_HasEndTimeButNoEndDateTime_ReturnsNull()
	{
		_orchestrator.Validate("PROJ-1", null, true, DateTime.Now, null)
			.Should().BeNull();
	}

	[Fact]
	public void Validate_EndEqualToStart_ReturnsError()
	{
		var time = new DateTime(2025, 1, 1, 10, 0, 0);
		_orchestrator.Validate("PROJ-1", null, true, time, time)
			.Should().NotBeNull();
	}

	[Fact]
	public async Task SaveNewAsync_Success_ReturnsSuccess()
	{
		var entry = WorkEntry.Reconstitute(1, "PROJ-1", DateTime.Now, null, null, false, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc");

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task SaveNewAsync_Failure_ReturnsFailure()
	{
		_mockStateService
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("Overlap detected"));

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc");

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("Overlap detected");
	}

	[Fact]
	public async Task SaveExistingAsync_Success_ReturnsSuccess()
	{
		_mockStateService
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());

		var result = await _orchestrator.SaveExistingAsync(1, "PROJ-1", DateTime.Now, null, "desc");

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task SaveExistingAsync_Failure_ReturnsFailure()
	{
		_mockStateService
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure("Not found"));

		var result = await _orchestrator.SaveExistingAsync(99, "PROJ-1", DateTime.Now, null, "desc");

		result.IsFailure.Should().BeTrue();
	}
}