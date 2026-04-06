using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Common;
using WorkTracker.Domain.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class WorkEntryEditOrchestratorTests
{
	private readonly Mock<IWorklogStateService> _mockStateService;
	private readonly Mock<IDialogService> _mockDialogService;
	private readonly Mock<ILocalizationService> _mockLocalization;
	private readonly WorkEntryEditOrchestrator _orchestrator;

	public WorkEntryEditOrchestratorTests()
	{
		_mockStateService = new Mock<IWorklogStateService>();
		_mockDialogService = new Mock<IDialogService>();
		_mockLocalization = new Mock<ILocalizationService>();
		_mockLocalization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_mockLocalization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
		_mockLocalization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] args) => string.Format(key, args));

		// Default: no overlaps
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new OverlapResolutionPlan());

		_orchestrator = new WorkEntryEditOrchestrator(
			_mockStateService.Object,
			_mockDialogService.Object,
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
	public async Task SaveNewAsync_NoOverlaps_Success_ReturnsTrue()
	{
		var entry = WorkEntry.Reconstitute(1, "PROJ-1", DateTime.Now, null, null, true, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
	}

	[Fact]
	public async Task SaveNewAsync_NoOverlaps_Failure_ReturnsFailure()
	{
		_mockStateService
			.Setup(s => s.CreateWorkEntryAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("Validation error"));

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("Validation error");
	}

	[Fact]
	public async Task SaveNewAsync_WithOverlaps_UserConfirms_ReturnsTrue()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [new OverlapAdjustment(2, "PROJ-2", null, OverlapAdjustmentKind.TrimEnd,
				new DateTime(2025, 1, 1, 9, 0, 0), new DateTime(2025, 1, 1, 11, 0, 0),
				null, new DateTime(2025, 1, 1, 10, 0, 0))]
		};
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(null, It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(plan);
		_mockDialogService
			.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(true);

		var entry = WorkEntry.Reconstitute(1, "PROJ-1", DateTime.Now, null, null, true, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryWithResolutionAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
	}

	[Fact]
	public async Task SaveNewAsync_WithOverlaps_UserCancels_ReturnsFalse()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [new OverlapAdjustment(2, "PROJ-2", null, OverlapAdjustmentKind.TrimEnd,
				new DateTime(2025, 1, 1, 9, 0, 0), new DateTime(2025, 1, 1, 11, 0, 0),
				null, new DateTime(2025, 1, 1, 10, 0, 0))]
		};
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(null, It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(plan);
		_mockDialogService
			.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(false);

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeFalse();
	}

	[Fact]
	public async Task SaveNewAsync_OverlapOnlyClosesActiveEntry_SkipsDialog_ReturnsTrue()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [new OverlapAdjustment(
				WorkEntryId: 2, TicketId: "PROJ-2", Description: null,
				Kind: OverlapAdjustmentKind.TrimEnd,
				OriginalStart: new DateTime(2025, 1, 1, 9, 0, 0), OriginalEnd: null,
				NewStart: null, NewEnd: new DateTime(2025, 1, 1, 10, 0, 0))]
		};
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(null, It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(plan);

		var entry = WorkEntry.Reconstitute(1, "PROJ-1", DateTime.Now, null, null, true, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryWithResolutionAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
		_mockDialogService.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
	}

	[Fact]
	public async Task SaveExistingAsync_NoOverlaps_Success_ReturnsTrue()
	{
		_mockStateService
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success());

		var result = await _orchestrator.SaveExistingAsync(1, "PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
	}

	[Fact]
	public async Task SaveExistingAsync_NoOverlaps_Failure_ReturnsFailure()
	{
		_mockStateService
			.Setup(s => s.UpdateWorkEntryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure("Not found"));

		var result = await _orchestrator.SaveExistingAsync(99, "PROJ-1", DateTime.Now, null, "desc", TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	#region Restart from history (SaveNewAsync with endTime=null)

	[Fact]
	public async Task SaveNewAsync_OpenEnded_WithActiveEntryOverlap_SkipsDialogAndReturnsTrue()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [new OverlapAdjustment(
				WorkEntryId: 5, TicketId: "PROJ-OLD", Description: "old task",
				Kind: OverlapAdjustmentKind.TrimEnd,
				OriginalStart: new DateTime(2025, 1, 1, 9, 0, 0), OriginalEnd: null,
				NewStart: null, NewEnd: new DateTime(2025, 1, 1, 14, 0, 0))]
		};
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(null, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(plan);

		var entry = WorkEntry.Reconstitute(10, "PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", true, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryWithResolutionAsync("PROJ-NEW", It.IsAny<DateTime>(), "new task", null, plan, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
		_mockDialogService.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		_mockStateService.Verify(s => s.CreateWorkEntryWithResolutionAsync("PROJ-NEW", It.IsAny<DateTime>(), "new task", null, plan, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveNewAsync_OpenEnded_WithClosedEntryOverlap_ShowsDialogAndRequiresConfirmation()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [new OverlapAdjustment(
				WorkEntryId: 5, TicketId: "PROJ-OLD", Description: "old task",
				Kind: OverlapAdjustmentKind.TrimEnd,
				OriginalStart: new DateTime(2025, 1, 1, 13, 0, 0), OriginalEnd: new DateTime(2025, 1, 1, 15, 0, 0),
				NewStart: null, NewEnd: new DateTime(2025, 1, 1, 14, 0, 0))]
		};
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(null, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(plan);
		_mockDialogService
			.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
			.ReturnsAsync(true);

		var entry = WorkEntry.Reconstitute(10, "PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", true, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryWithResolutionAsync("PROJ-NEW", It.IsAny<DateTime>(), "new task", null, plan, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
		_mockDialogService.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
	}

	[Fact]
	public async Task SaveNewAsync_OpenEnded_NoOverlaps_CreatesWithoutResolution()
	{
		var entry = WorkEntry.Reconstitute(10, "PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", true, DateTime.MinValue);
		_mockStateService
			.Setup(s => s.CreateWorkEntryAsync("PROJ-NEW", It.IsAny<DateTime>(), "new task", null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Success(entry));

		var result = await _orchestrator.SaveNewAsync("PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeTrue();
		_mockStateService.Verify(s => s.CreateWorkEntryAsync("PROJ-NEW", It.IsAny<DateTime>(), "new task", null, It.IsAny<CancellationToken>()), Times.Once);
		_mockStateService.Verify(s => s.CreateWorkEntryWithResolutionAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	#endregion

	#region Restart from history (SaveNewAsync with resolution failure)

	[Fact]
	public async Task SaveNewAsync_OpenEnded_WithResolutionFailure_ReturnsFailure()
	{
		var plan = new OverlapResolutionPlan
		{
			Adjustments = [new OverlapAdjustment(
				WorkEntryId: 5, TicketId: "PROJ-OLD", Description: null,
				Kind: OverlapAdjustmentKind.TrimEnd,
				OriginalStart: new DateTime(2025, 1, 1, 9, 0, 0), OriginalEnd: null,
				NewStart: null, NewEnd: new DateTime(2025, 1, 1, 14, 0, 0))]
		};
		_mockStateService
			.Setup(s => s.ComputeOverlapResolutionAsync(null, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(plan);
		_mockStateService
			.Setup(s => s.CreateWorkEntryWithResolutionAsync(It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<OverlapResolutionPlan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Failure<WorkEntry>("Database error"));

		var result = await _orchestrator.SaveNewAsync("PROJ-NEW", new DateTime(2025, 1, 1, 14, 0, 0), null, "new task", TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("Database error");
	}

	#endregion
}
