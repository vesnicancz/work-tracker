using FluentAssertions;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Domain.Tests.Entities;

public class WorkEntryTests
{
	[Fact]
	public void IsValid_WithTicketId_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = WorkEntry.Create("PROJ-123", DateTime.Now, DateTime.Now.AddHours(1), null, DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void IsValid_WithDescription_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = WorkEntry.Create(null, DateTime.Now, DateTime.Now.AddHours(1), "Working on feature", DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void IsValid_WithBothTicketIdAndDescription_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = WorkEntry.Create("PROJ-123", DateTime.Now, DateTime.Now.AddHours(1), "Working on feature", DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void IsValid_WithoutTicketIdAndDescription_ShouldReturnFalse()
	{
		// Arrange
		var workEntry = WorkEntry.Create(null, DateTime.Now, DateTime.Now.AddHours(1), null, DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithWhitespaceTicketIdAndDescription_ShouldReturnFalse()
	{
		// Arrange
		var workEntry = WorkEntry.Create("   ", DateTime.Now, DateTime.Now.AddHours(1), "   ", DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithEndTimeBeforeStartTime_ShouldReturnFalse()
	{
		// Arrange
		var workEntry = WorkEntry.Create("PROJ-123", DateTime.Now, DateTime.Now.AddHours(-1), null, DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithNullEndTime_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = WorkEntry.Create("PROJ-123", DateTime.Now, null, null, DateTime.Now);

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void Duration_WithValidEndTime_ShouldCalculateCorrectly()
	{
		// Arrange
		var startTime = new DateTime(2025, 11, 2, 9, 0, 0);
		var endTime = new DateTime(2025, 11, 2, 11, 30, 0);
		var workEntry = WorkEntry.Create("PROJ-123", startTime, endTime, null, startTime);

		// Act
		var duration = workEntry.Duration;

		// Assert
		duration.Should().NotBeNull();
		duration.Value.Should().Be(TimeSpan.FromHours(2.5));
	}

	[Fact]
	public void Duration_WithNullEndTime_ShouldReturnNull()
	{
		// Arrange
		var workEntry = WorkEntry.Create("PROJ-123", DateTime.Now, null, null, DateTime.Now);

		// Act
		var duration = workEntry.Duration;

		// Assert
		duration.Should().BeNull();
	}

	[Fact]
	public void Duration_WithZeroDuration_ShouldReturnZero()
	{
		// Arrange
		var time = DateTime.Now;
		var workEntry = WorkEntry.Create("PROJ-123", time, time, null, time);

		// Act
		var duration = workEntry.Duration;

		// Assert
		duration.Should().NotBeNull();
		duration.Value.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void Create_SetsCreatedAt()
	{
		// Arrange
		var now = DateTime.Now;
		var workEntry = WorkEntry.Create(null, DateTime.MinValue, null, null, now);

		// Act & Assert
		workEntry.CreatedAt.Should().Be(now);
	}

	[Fact]
	public void Reconstitute_SetsUpdatedAt()
	{
		// Arrange
		var now = DateTime.Now;
		var workEntry = WorkEntry.Reconstitute(0, null, DateTime.MinValue, null, null, true, DateTime.MinValue, now);

		// Act & Assert
		workEntry.UpdatedAt.Should().NotBeNull();
		workEntry.UpdatedAt.Should().Be(now);
	}

	[Fact]
	public void Create_SetsUpdatedAtToNull()
	{
		// Arrange & Act
		var workEntry = WorkEntry.Create(null, DateTime.MinValue, null, null, DateTime.MinValue);

		// Assert
		workEntry.UpdatedAt.Should().BeNull();
	}

	[Fact]
	public void Stop_SetsEndTimeAndIsActiveAndUpdatedAt()
	{
		// Arrange
		var startTime = new DateTime(2026, 3, 25, 9, 0, 0);
		var endTime = new DateTime(2026, 3, 25, 11, 0, 0);
		var now = new DateTime(2026, 3, 25, 11, 0, 0);
		var workEntry = WorkEntry.Reconstitute(0, "PROJ-123", startTime, null, null, true, startTime);

		// Act
		workEntry.Stop(endTime, now);

		// Assert
		workEntry.EndTime.Should().Be(endTime);
		workEntry.IsActive.Should().BeFalse();
		workEntry.UpdatedAt.Should().Be(now);
	}

	[Fact]
	public void Stop_PreservesOtherFields()
	{
		// Arrange
		var startTime = new DateTime(2026, 3, 25, 9, 0, 0);
		var createdAt = new DateTime(2026, 3, 25, 9, 0, 0);
		var workEntry = WorkEntry.Reconstitute(42, "PROJ-999", startTime, null, "Important task", true, createdAt);

		// Act
		workEntry.Stop(new DateTime(2026, 3, 25, 11, 0, 0), new DateTime(2026, 3, 25, 11, 0, 0));

		// Assert
		workEntry.Id.Should().Be(42);
		workEntry.TicketId.Should().Be("PROJ-999");
		workEntry.StartTime.Should().Be(startTime);
		workEntry.Description.Should().Be("Important task");
		workEntry.CreatedAt.Should().Be(createdAt);
	}

	[Fact]
	public void UpdateFields_UpdatesAllFields()
	{
		// Arrange
		var originalStart = new DateTime(2026, 3, 25, 8, 0, 0);
		var newStart = new DateTime(2026, 3, 25, 9, 0, 0);
		var newEnd = new DateTime(2026, 3, 25, 11, 0, 0);
		var now = new DateTime(2026, 3, 25, 11, 0, 0);
		var workEntry = WorkEntry.Reconstitute(0, "OLD-001", originalStart, null, "Old description", true, DateTime.MinValue);

		// Act
		workEntry.UpdateFields("NEW-002", newStart, newEnd, "New description", now);

		// Assert
		workEntry.TicketId.Should().Be("NEW-002");
		workEntry.StartTime.Should().Be(newStart);
		workEntry.EndTime.Should().Be(newEnd);
		workEntry.Description.Should().Be("New description");
		workEntry.UpdatedAt.Should().Be(now);
	}

	[Fact]
	public void UpdateFields_WithEndTime_SetsIsActiveFalse()
	{
		// Arrange
		var workEntry = WorkEntry.Reconstitute(0, "PROJ-123", new DateTime(2026, 3, 25, 9, 0, 0), null, null, true, DateTime.MinValue);

		// Act
		workEntry.UpdateFields("PROJ-123", null, new DateTime(2026, 3, 25, 11, 0, 0), null, new DateTime(2026, 3, 25, 11, 0, 0));

		// Assert
		workEntry.IsActive.Should().BeFalse();
	}

	[Fact]
	public void UpdateFields_WithoutEndTime_SetsIsActiveTrue()
	{
		// Arrange
		var workEntry = WorkEntry.Reconstitute(0, "PROJ-123", new DateTime(2026, 3, 25, 9, 0, 0), new DateTime(2026, 3, 25, 10, 0, 0), null, false, DateTime.MinValue);

		// Act
		workEntry.UpdateFields("PROJ-123", null, null, null, new DateTime(2026, 3, 25, 11, 0, 0));

		// Assert
		workEntry.IsActive.Should().BeTrue();
		workEntry.EndTime.Should().BeNull();
	}

	[Fact]
	public void UpdateFields_WithoutStartTime_PreservesStartTime()
	{
		// Arrange
		var originalStart = new DateTime(2026, 3, 25, 9, 0, 0);
		var workEntry = WorkEntry.Reconstitute(0, "PROJ-123", originalStart, null, null, true, DateTime.MinValue);

		// Act
		workEntry.UpdateFields("PROJ-123", null, null, "Updated description", new DateTime(2026, 3, 25, 11, 0, 0));

		// Assert
		workEntry.StartTime.Should().Be(originalStart);
	}

	[Fact]
	public void Create_SetsAllFields()
	{
		// Arrange
		var startTime = new DateTime(2026, 3, 25, 9, 0, 0);
		var endTime = new DateTime(2026, 3, 25, 11, 0, 0);
		var now = new DateTime(2026, 3, 25, 9, 0, 0);

		// Act
		var workEntry = WorkEntry.Create("PROJ-123", startTime, endTime, "Test description", now);

		// Assert
		workEntry.TicketId.Should().Be("PROJ-123");
		workEntry.StartTime.Should().Be(startTime);
		workEntry.EndTime.Should().Be(endTime);
		workEntry.Description.Should().Be("Test description");
		workEntry.CreatedAt.Should().Be(now);
	}

	[Fact]
	public void Create_WithEndTime_SetsIsActiveFalse()
	{
		// Arrange
		var startTime = new DateTime(2026, 3, 25, 9, 0, 0);
		var endTime = new DateTime(2026, 3, 25, 11, 0, 0);
		var now = new DateTime(2026, 3, 25, 9, 0, 0);

		// Act
		var workEntry = WorkEntry.Create("PROJ-123", startTime, endTime, null, now);

		// Assert
		workEntry.IsActive.Should().BeFalse();
	}

	[Fact]
	public void Create_WithoutEndTime_SetsIsActiveTrue()
	{
		// Arrange
		var startTime = new DateTime(2026, 3, 25, 9, 0, 0);
		var now = new DateTime(2026, 3, 25, 9, 0, 0);

		// Act
		var workEntry = WorkEntry.Create("PROJ-123", startTime, null, null, now);

		// Assert
		workEntry.IsActive.Should().BeTrue();
		workEntry.EndTime.Should().BeNull();
	}
}