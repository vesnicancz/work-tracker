using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Models;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;

namespace WorkTracker.Application.Tests.Services;

public class WorkEntryServiceTests
{
	private readonly Mock<IWorkEntryRepository> _mockRepository;
	private readonly Mock<ILogger<WorkEntryService>> _mockLogger;
	private readonly WorkEntryService _service;

	public WorkEntryServiceTests()
	{
		_mockRepository = new Mock<IWorkEntryRepository>();
		_mockLogger = new Mock<ILogger<WorkEntryService>>();
		_service = new WorkEntryService(_mockRepository.Object, TimeProvider.System, _mockLogger.Object);
	}

	[Fact]
	public async Task StartWorkAsync_WithValidTicketId_ShouldReturnSuccess()
	{
		// Arrange
		var ticketId = "PROJ-123";
		var description = "Working on feature";
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);
		_mockRepository
			.Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);
		_mockRepository
			.Setup(r => r.AddAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry entry, CancellationToken _) => entry);

		// Act
		var result = await _service.StartWorkAsync(ticketId, null, description, cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.TicketId.Should().Be(ticketId);
		result.Value.Description.Should().Be(description);
		result.Value.IsActive.Should().BeTrue();
		_mockRepository.Verify(r => r.AddAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StartWorkAsync_WithoutTicketIdAndDescription_ShouldReturnFailure()
	{
		// Arrange
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		// Act
		var result = await _service.StartWorkAsync(null, null, null, cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("Both ticket ID and description cannot be empty");
		_mockRepository.Verify(r => r.AddAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task StartWorkAsync_WithActiveEntry_ShouldAutoStopPrevious()
	{
		// Arrange
		var existingEntry = WorkEntry.Reconstitute(1, "PROJ-100", DateTime.Now.AddHours(-2), null, null, true, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(existingEntry);
		_mockRepository
			.Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);
		_mockRepository
			.Setup(r => r.AddAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry entry, CancellationToken _) => entry);

		// Act
		var result = await _service.StartWorkAsync("PROJ-123", null, "New work", cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsSuccess.Should().BeTrue();
		_mockRepository.Verify(r => r.UpdateAsync(It.Is<WorkEntry>(e =>
			e.Id == 1 && e.IsActive == false && e.EndTime.HasValue), It.IsAny<CancellationToken>()), Times.Once);
		_mockRepository.Verify(r => r.AddAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StartWorkAsync_WithOverlappingEntry_ShouldReturnFailure()
	{
		// Arrange
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);
		_mockRepository
			.Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		// Act
		var result = await _service.StartWorkAsync("PROJ-123", null, "Test", cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("overlaps");
		_mockRepository.Verify(r => r.AddAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task StopWorkAsync_WithActiveEntry_ShouldReturnSuccess()
	{
		// Arrange
		var activeEntry = WorkEntry.Reconstitute(1, "PROJ-123", DateTime.Now.AddHours(-2), null, null, true, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(activeEntry);
		_mockRepository
			.Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		// Act
		var result = await _service.StopWorkAsync(cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.IsActive.Should().BeFalse();
		result.Value.EndTime.Should().NotBeNull();
		_mockRepository.Verify(r => r.UpdateAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task StopWorkAsync_WithoutActiveEntry_ShouldReturnFailure()
	{
		// Arrange
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		// Act
		var result = await _service.StopWorkAsync(cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("No active work entry found");
		_mockRepository.Verify(r => r.UpdateAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task StopWorkAsync_WithEndTimeBeforeStartTime_ShouldReturnFailure()
	{
		// Arrange
		var activeEntry = WorkEntry.Reconstitute(1, "PROJ-123", DateTime.Now, null, null, true, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(activeEntry);

		// Act
		var result = await _service.StopWorkAsync(DateTime.Now.AddHours(-1), TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("Invalid end time");
		_mockRepository.Verify(r => r.UpdateAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetActiveWorkAsync_ShouldReturnActiveEntry()
	{
		// Arrange
		var activeEntry = WorkEntry.Reconstitute(1, "PROJ-123", DateTime.Now, null, null, true, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetActiveWorkEntryAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(activeEntry);

		// Act
		var result = await _service.GetActiveWorkAsync(TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result!.IsActive.Should().BeTrue();
		result.TicketId.Should().Be("PROJ-123");
	}

	[Fact]
	public async Task UpdateWorkEntryAsync_WithValidData_ShouldReturnSuccess()
	{
		// Arrange
		var existingEntry = WorkEntry.Reconstitute(1, "PROJ-123", DateTime.Now.AddHours(-2), DateTime.Now, null, false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
			.ReturnsAsync(existingEntry);
		_mockRepository
			.Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		// Act
		var result = await _service.UpdateWorkEntryAsync(1, "PROJ-456", DateTime.Now.AddHours(-3), DateTime.Now.AddHours(-1), "Updated", cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.TicketId.Should().Be("PROJ-456");
		result.Value.Description.Should().Be("Updated");
		_mockRepository.Verify(r => r.UpdateAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task UpdateWorkEntryAsync_WithNonExistentId_ShouldReturnFailure()
	{
		// Arrange
		_mockRepository
			.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		// Act
		var result = await _service.UpdateWorkEntryAsync(999, "PROJ-123", cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("not found");
		_mockRepository.Verify(r => r.UpdateAsync(It.IsAny<WorkEntry>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task DeleteWorkEntryAsync_WithValidId_ShouldReturnSuccess()
	{
		// Arrange
		var existingEntry = WorkEntry.Reconstitute(1, "PROJ-123", DateTime.Now, null, null, false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
			.ReturnsAsync(existingEntry);

		// Act
		var result = await _service.DeleteWorkEntryAsync(1, TestContext.Current.CancellationToken);

		// Assert
		result.IsSuccess.Should().BeTrue();
		_mockRepository.Verify(r => r.DeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task DeleteWorkEntryAsync_WithNonExistentId_ShouldReturnFailure()
	{
		// Arrange
		_mockRepository
			.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
			.ReturnsAsync((WorkEntry?)null);

		// Act
		var result = await _service.DeleteWorkEntryAsync(999, TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Contain("not found");
		_mockRepository.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task GetWorkEntriesByDateAsync_ShouldReturnEntries()
	{
		// Arrange
		var date = DateTime.Today;
		var entries = new List<WorkEntry>
		{
			WorkEntry.Reconstitute(1, "PROJ-123", date.AddHours(9), null, null, true, DateTime.MinValue),
			WorkEntry.Reconstitute(2, "PROJ-124", date.AddHours(13), null, null, true, DateTime.MinValue)
		};
		_mockRepository
			.Setup(r => r.GetByDateAsync(date, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		// Act
		var result = await _service.GetWorkEntriesByDateAsync(date, TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result.Should().HaveCount(2);
		result.First().TicketId.Should().Be("PROJ-123");
	}

	[Fact]
	public async Task GetWorkEntriesByDateRangeAsync_ShouldReturnEntries()
	{
		// Arrange
		var startDate = DateTime.Today;
		var endDate = DateTime.Today.AddDays(7);
		var entries = new List<WorkEntry>
		{
			WorkEntry.Reconstitute(1, "PROJ-123", startDate, null, null, true, DateTime.MinValue),
			WorkEntry.Reconstitute(2, "PROJ-124", startDate.AddDays(3), null, null, true, DateTime.MinValue)
		};
		_mockRepository
			.Setup(r => r.GetByDateRangeAsync(startDate, endDate, It.IsAny<CancellationToken>()))
			.ReturnsAsync(entries);

		// Act
		var result = await _service.GetWorkEntriesByDateRangeAsync(startDate, endDate, TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result.Should().HaveCount(2);
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_NoOverlaps_ReturnsEmptyPlan()
	{
		// Arrange
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry>());

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeFalse();
		plan.Adjustments.Should().BeEmpty();
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_HeadOverlap_ReturnsTrimEnd()
	{
		// Arrange
		// Existing: 9:00-11:00, candidate: 10:00-12:00 → existing end overlaps with candidate start → TrimEnd existing to 10:00
		var existing = WorkEntry.Reconstitute(1, "PROJ-100", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), "Existing", false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { existing });

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(1);
		var adjustment = plan.Adjustments[0];
		adjustment.WorkEntryId.Should().Be(1);
		adjustment.Kind.Should().Be(OverlapAdjustmentKind.TrimEnd);
		adjustment.NewEnd.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0));
		adjustment.OriginalStart.Should().Be(new DateTime(2026, 1, 15, 9, 0, 0));
		adjustment.OriginalEnd.Should().Be(new DateTime(2026, 1, 15, 11, 0, 0));
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_TailOverlap_ReturnsTrimStart()
	{
		// Arrange
		// Existing: 10:00-12:00, candidate: 9:00-11:00 → candidate end overlaps with existing start → TrimStart existing to 11:00
		var existing = WorkEntry.Reconstitute(2, "PROJ-101", new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), "Existing", false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { existing });

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(1);
		var adjustment = plan.Adjustments[0];
		adjustment.WorkEntryId.Should().Be(2);
		adjustment.Kind.Should().Be(OverlapAdjustmentKind.TrimStart);
		adjustment.NewStart.Should().Be(new DateTime(2026, 1, 15, 11, 0, 0));
		adjustment.OriginalStart.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0));
		adjustment.OriginalEnd.Should().Be(new DateTime(2026, 1, 15, 12, 0, 0));
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_CompleteCover_ReturnsDelete()
	{
		// Arrange
		// Existing: 10:00-10:30, candidate: 9:00-11:00 → candidate fully contains existing → Delete
		var existing = WorkEntry.Reconstitute(3, "PROJ-102", new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 10, 30, 0), "Short task", false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { existing });

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(1);
		var adjustment = plan.Adjustments[0];
		adjustment.WorkEntryId.Should().Be(3);
		adjustment.Kind.Should().Be(OverlapAdjustmentKind.Delete);
		adjustment.NewStart.Should().BeNull();
		adjustment.NewEnd.Should().BeNull();
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_CandidateInsideExisting_ReturnsSplit()
	{
		// Arrange
		// Existing: 9:00-15:00, candidate: 11:00-13:00 → candidate is inside existing → Split into 9:00-11:00 and 13:00-15:00
		var existing = WorkEntry.Reconstitute(4, "PROJ-103", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 15, 0, 0), "Long task", false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { existing });

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 11, 0, 0), new DateTime(2026, 1, 15, 13, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(1);
		var adjustment = plan.Adjustments[0];
		adjustment.WorkEntryId.Should().Be(4);
		adjustment.Kind.Should().Be(OverlapAdjustmentKind.Split);
		// NewEnd = candidateStart = first half ends at 11:00
		adjustment.NewEnd.Should().Be(new DateTime(2026, 1, 15, 11, 0, 0));
		// NewStart = candidateEnd = second half starts at 13:00
		adjustment.NewStart.Should().Be(new DateTime(2026, 1, 15, 13, 0, 0));
		adjustment.OriginalStart.Should().Be(new DateTime(2026, 1, 15, 9, 0, 0));
		adjustment.OriginalEnd.Should().Be(new DateTime(2026, 1, 15, 15, 0, 0));
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_TrimResultsTooShort_PromotesToDelete()
	{
		// Arrange
		// Existing: 10:00-10:00:30 (30 seconds long, sub-minute), candidate: 9:00-11:00
		// After rounding candidate times: 9:00-11:00. Existing is fully covered (candidateStart <= existingStart && candidateEnd >= existingEnd) → Delete
		// Use an existing entry where remaining time after trim would be < 1 minute:
		// Existing: 10:00-10:00 (same start/end — degenerate), or use existing 9:59-10:01 with candidate 10:00-11:00
		// → remaining = candidateStart(10:00) - existingStart(9:59) = 1 minute exactly, not < 1 min
		// Use existing 9:59:30-10:01 reconstituted with seconds, candidate 10:00-11:00
		// → remaining after TrimEnd = 10:00 - 9:59:30 = 30 seconds < 1 minute → Delete
		var existing = WorkEntry.Reconstitute(5, "PROJ-104", new DateTime(2026, 1, 15, 9, 59, 30), new DateTime(2026, 1, 15, 10, 1, 0), "Tiny task", false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { existing });

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(1);
		var adjustment = plan.Adjustments[0];
		adjustment.WorkEntryId.Should().Be(5);
		adjustment.Kind.Should().Be(OverlapAdjustmentKind.Delete);
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_ActiveEntry_WithCompletedCandidate_ReturnsTrimEnd()
	{
		// Arrange
		// Existing active entry from 9:00 (no EndTime → MaxValue).
		// Candidate is a completed entry 10:00-11:00 (e.g. calendar meeting).
		// Active entries are never split — they get TrimEnd instead.
		var existingActive = WorkEntry.Reconstitute(6, "PROJ-105", new DateTime(2026, 1, 15, 9, 0, 0), null, "Active task", true, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { existingActive });

		// Act — completed candidate (with endTime) inserted while active entry runs
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), TestContext.Current.CancellationToken);

		// Assert — active entry gets TrimEnd (stopped), not Split
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(1);
		var adjustment = plan.Adjustments[0];
		adjustment.WorkEntryId.Should().Be(6);
		adjustment.Kind.Should().Be(OverlapAdjustmentKind.TrimEnd);
		adjustment.NewEnd.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0));
		adjustment.OriginalEnd.Should().BeNull();
	}

	[Fact]
	public async Task ComputeOverlapResolutionAsync_MultipleOverlaps_ReturnsMultipleAdjustments()
	{
		// Arrange
		// Entry A: 8:00-10:30 → head overlap with candidate 10:00-12:00 → TrimEnd to 10:00
		// Entry B: 11:00-13:00 → tail overlap with candidate 10:00-12:00 → TrimStart to 12:00
		var entryA = WorkEntry.Reconstitute(7, "PROJ-106", new DateTime(2026, 1, 15, 8, 0, 0), new DateTime(2026, 1, 15, 10, 30, 0), "Task A", false, DateTime.MinValue);
		var entryB = WorkEntry.Reconstitute(8, "PROJ-107", new DateTime(2026, 1, 15, 11, 0, 0), new DateTime(2026, 1, 15, 13, 0, 0), "Task B", false, DateTime.MinValue);
		_mockRepository
			.Setup(r => r.GetOverlappingEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<WorkEntry> { entryA, entryB });

		// Act
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), TestContext.Current.CancellationToken);

		// Assert
		plan.HasAdjustments.Should().BeTrue();
		plan.Adjustments.Should().HaveCount(2);

		var adjustmentA = plan.Adjustments.Single(a => a.WorkEntryId == 7);
		adjustmentA.Kind.Should().Be(OverlapAdjustmentKind.TrimEnd);
		adjustmentA.NewEnd.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0));

		var adjustmentB = plan.Adjustments.Single(a => a.WorkEntryId == 8);
		adjustmentB.Kind.Should().Be(OverlapAdjustmentKind.TrimStart);
		adjustmentB.NewStart.Should().Be(new DateTime(2026, 1, 15, 12, 0, 0));
	}
}