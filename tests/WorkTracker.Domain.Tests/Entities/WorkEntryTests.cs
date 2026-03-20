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
}
