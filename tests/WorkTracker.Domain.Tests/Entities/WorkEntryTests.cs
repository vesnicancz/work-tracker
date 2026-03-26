using FluentAssertions;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Domain.Tests.Entities;

public class WorkEntryTests
{
	[Fact]
	public void IsValid_WithTicketId_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1)
		};

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void IsValid_WithDescription_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			Description = "Working on feature",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1)
		};

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void IsValid_WithBothTicketIdAndDescription_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			Description = "Working on feature",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1)
		};

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void IsValid_WithoutTicketIdAndDescription_ShouldReturnFalse()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1)
		};

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithWhitespaceTicketIdAndDescription_ShouldReturnFalse()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			TicketId = "   ",
			Description = "   ",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1)
		};

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithEndTimeBeforeStartTime_ShouldReturnFalse()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(-1)
		};

		// Act
		var result = workEntry.IsValid();

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithNullEndTime_ShouldReturnTrue()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = null
		};

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
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = startTime,
			EndTime = endTime
		};

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
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = null
		};

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
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = time,
			EndTime = time
		};

		// Act
		var duration = workEntry.Duration;

		// Assert
		duration.Should().NotBeNull();
		duration.Value.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void IsActive_DefaultValue_ShouldBeFalse()
	{
		// Arrange & Act
		var workEntry = new WorkEntry();

		// Assert
		workEntry.IsActive.Should().BeFalse();
	}

	[Fact]
	public void CreatedAt_ShouldBeSettable()
	{
		// Arrange
		var now = DateTime.Now;
		var workEntry = new WorkEntry
		{
			CreatedAt = now
		};

		// Act & Assert
		workEntry.CreatedAt.Should().Be(now);
	}

	[Fact]
	public void UpdatedAt_ShouldBeNullableAndSettable()
	{
		// Arrange
		var now = DateTime.Now;
		var workEntry = new WorkEntry
		{
			UpdatedAt = now
		};

		// Act & Assert
		workEntry.UpdatedAt.Should().NotBeNull();
		workEntry.UpdatedAt.Should().Be(now);
	}

	[Fact]
	public void UpdatedAt_DefaultValue_ShouldBeNull()
	{
		// Arrange & Act
		var workEntry = new WorkEntry();

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
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = startTime,
			IsActive = true
		};

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
		var workEntry = new WorkEntry
		{
			Id = 42,
			TicketId = "PROJ-999",
			StartTime = startTime,
			Description = "Important task",
			IsActive = true,
			CreatedAt = createdAt
		};

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
		var workEntry = new WorkEntry
		{
			TicketId = "OLD-001",
			StartTime = originalStart,
			Description = "Old description",
			IsActive = true
		};

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
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = new DateTime(2026, 3, 25, 9, 0, 0),
			IsActive = true
		};

		// Act
		workEntry.UpdateFields("PROJ-123", null, new DateTime(2026, 3, 25, 11, 0, 0), null, new DateTime(2026, 3, 25, 11, 0, 0));

		// Assert
		workEntry.IsActive.Should().BeFalse();
	}

	[Fact]
	public void UpdateFields_WithoutEndTime_SetsIsActiveTrue()
	{
		// Arrange
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = new DateTime(2026, 3, 25, 9, 0, 0),
			EndTime = new DateTime(2026, 3, 25, 10, 0, 0),
			IsActive = false
		};

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
		var workEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = originalStart
		};

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