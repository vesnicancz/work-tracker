using FluentAssertions;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;

namespace WorkTracker.Application.Tests.Services;

public class WorklogValidatorTests
{
	private readonly WorklogValidator _validator;

	public WorklogValidatorTests()
	{
		_validator = new WorklogValidator();
	}

	#region Validate (WorklogDto) Tests

	[Fact]
	public void Validate_WithValidWorklog_ShouldReturnSuccess()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now.AddHours(-2),
			EndTime = DateTime.Now,
			DurationMinutes = 120,
			Description = "Working on feature"
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Fact]
	public void Validate_WithStartTimeAfterEndTime_ShouldReturnFailure()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(-1),
			DurationMinutes = 60
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("End time cannot be before start time"));
	}

	[Fact]
	public void Validate_WithZeroDuration_ShouldReturnFailure()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1),
			DurationMinutes = 0
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Duration must be greater than 0"));
	}

	[Fact]
	public void Validate_WithNegativeDuration_ShouldReturnFailure()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1),
			DurationMinutes = -10
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Duration must be greater than 0"));
	}

	[Fact]
	public void Validate_WithDurationExceeding24Hours_ShouldReturnFailure()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1),
			DurationMinutes = 1441 // 24 hours + 1 minute
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("cannot exceed 24 hours"));
	}

	[Fact]
	public void Validate_WithDurationEditedAwayFromTheTimes_ShouldReturnSuccess()
	{
		var start = new DateTime(2025, 11, 2, 9, 0, 0);
		var worklog = new WorklogDto { TicketId = "PROJ-123", StartTime = start, EndTime = start.AddHours(2), DurationMinutes = 60 };

		_validator.Validate(worklog).IsValid.Should().BeTrue();
	}

	[Fact]
	public void Validate_WithSameStartAndEnd_ShouldReturnSuccess()
	{
		// An aggregated row, or an entry started and stopped within one minute
		var start = new DateTime(2025, 11, 2, 9, 0, 0);
		var worklog = new WorklogDto { TicketId = "PROJ-123", StartTime = start, EndTime = start, DurationMinutes = 1 };

		_validator.Validate(worklog).IsValid.Should().BeTrue();
	}

	[Fact]
	public void Validate_WithoutTicket_ShouldReturnSuccess()
	{
		// Whether a ticket is required is up to the plugin
		var start = new DateTime(2025, 11, 2, 9, 0, 0);
		var worklog = new WorklogDto { Description = "Meeting", StartTime = start, EndTime = start.AddHours(1), DurationMinutes = 60 };

		_validator.Validate(worklog).IsValid.Should().BeTrue();
	}

	[Fact]
	public void Validate_WithMultipleErrors_ShouldReturnAllErrors()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(-1), // Invalid
			DurationMinutes = -10 // Invalid
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().HaveCountGreaterThan(1);
	}

	#endregion Validate (WorklogDto) Tests
}
