using FluentAssertions;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Tests.Services;

public class WorklogValidatorTests
{
	private readonly WorklogValidator _validator;

	public WorklogValidatorTests()
	{
		_validator = new WorklogValidator();
	}

	#region ValidateForSubmission Tests

	[Fact]
	public void ValidateForSubmission_WithValidEntry_ShouldReturnSuccess()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-123", DateTime.Now.AddHours(-2), DateTime.Now, "Working on feature", DateTime.Now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeTrue();
	}

	[Fact]
	public void ValidateForSubmission_WithoutTicketId_ShouldReturnFailure()
	{
		// Arrange
		var entry = WorkEntry.Create(null, DateTime.Now.AddHours(-2), DateTime.Now, "Working on feature", DateTime.Now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Ticket ID is required"));
	}

	[Fact]
	public void ValidateForSubmission_WithWhitespaceTicketId_ShouldReturnFailure()
	{
		// Arrange
		var entry = WorkEntry.Create("   ", DateTime.Now.AddHours(-2), DateTime.Now, null, DateTime.Now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Ticket ID is required"));
	}

	[Fact]
	public void ValidateForSubmission_WithActiveEntry_ShouldReturnFailure()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-123", DateTime.Now.AddHours(-2), null, null, DateTime.Now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("still active"));
	}

	[Fact]
	public void ValidateForSubmission_WithStartTimeAfterEndTime_ShouldReturnFailure()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-123", DateTime.Now, DateTime.Now.AddHours(-1), null, DateTime.Now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Start time must be before end time"));
	}

	[Fact]
	public void ValidateForSubmission_WithZeroDuration_ShouldReturnFailure()
	{
		// Arrange
		var now = DateTime.Now;
		var entry = WorkEntry.Create("PROJ-123", now, now, null, now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Start time must be before end time"));
	}

	[Fact]
	public void ValidateForSubmission_WithDurationLessThanOneSecond_ShouldReturnFailure()
	{
		// Arrange
		var startTime = DateTime.Now;
		var entry = WorkEntry.Create("PROJ-123", startTime, startTime.AddMilliseconds(500), null, startTime);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("at least 1 second"));
	}

	[Fact]
	public void ValidateForSubmission_WithDurationExceeding24Hours_ShouldReturnFailure()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-123", DateTime.Now, DateTime.Now.AddHours(25), null, DateTime.Now);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("cannot exceed 24 hours"));
	}

	[Fact]
	public void ValidateForSubmission_WithExactly24Hours_ShouldReturnSuccess()
	{
		// Arrange
		var startTime = DateTime.Now;
		var entry = WorkEntry.Create("PROJ-123", startTime, startTime.AddHours(24), null, startTime);

		// Act
		var result = _validator.ValidateForSubmission(entry);

		// Assert
		result.IsValid.Should().BeTrue();
	}

	#endregion ValidateForSubmission Tests

	#region ValidateMultiple Tests

	[Fact]
	public void ValidateMultiple_WithValidEntries_ShouldReturnSuccess()
	{
		// Arrange
		var entries = new List<WorkEntry>
		{
			WorkEntry.Create("PROJ-123", DateTime.Now.AddHours(-3), DateTime.Now.AddHours(-2), null, DateTime.Now),
			WorkEntry.Create("PROJ-124", DateTime.Now.AddHours(-1), DateTime.Now, null, DateTime.Now)
		};

		// Act
		var result = _validator.ValidateMultiple(entries);

		// Assert
		result.IsValid.Should().BeTrue();
	}

	[Fact]
	public void ValidateMultiple_WithEmptyList_ShouldReturnFailure()
	{
		// Arrange
		var entries = new List<WorkEntry>();

		// Act
		var result = _validator.ValidateMultiple(entries);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("No work entries provided"));
	}

	[Fact]
	public void ValidateMultiple_WithSomeInvalidEntries_ShouldReturnFailure()
	{
		// Arrange
		var entries = new List<WorkEntry>
		{
			WorkEntry.Create("PROJ-123", DateTime.Now.AddHours(-2), DateTime.Now, null, DateTime.Now),
			WorkEntry.Create("PROJ-124", DateTime.Now, null, null, DateTime.Now) // Active entry
		};

		// Act
		var result = _validator.ValidateMultiple(entries);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("still active"));
	}

	[Fact]
	public void ValidateMultiple_WithAllInvalidEntries_ShouldReturnMultipleErrors()
	{
		// Arrange
		var entries = new List<WorkEntry>
		{
			WorkEntry.Create(null, DateTime.Now, DateTime.Now.AddHours(1), null, DateTime.Now), // Missing TicketId
			WorkEntry.Create("PROJ-124", DateTime.Now, null, null, DateTime.Now) // Active entry
		};

		// Act
		var result = _validator.ValidateMultiple(entries);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().HaveCountGreaterThan(1);
	}

	#endregion ValidateMultiple Tests

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
	public void Validate_WithNullWorklog_ShouldReturnFailure()
	{
		// Act
		var result = _validator.Validate(null!);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("cannot be null"));
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
		result.Errors.Should().Contain(e => e.Contains("Start time must be before end time"));
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
	public void Validate_WithDurationMismatch_ShouldReturnFailure()
	{
		// Arrange
		var startTime = new DateTime(2025, 11, 2, 9, 0, 0);
		var endTime = new DateTime(2025, 11, 2, 11, 0, 0); // 2 hours = 120 minutes
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = startTime,
			EndTime = endTime,
			DurationMinutes = 60 // Wrong - should be 120
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Contains("Duration mismatch"));
	}

	[Fact]
	public void Validate_WithSlightDurationMismatch_ShouldReturnSuccess()
	{
		// Arrange - 1 minute tolerance
		var startTime = new DateTime(2025, 11, 2, 9, 0, 0);
		var endTime = new DateTime(2025, 11, 2, 11, 0, 30); // 120.5 minutes
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			StartTime = startTime,
			EndTime = endTime,
			DurationMinutes = 120 // Within 1 minute tolerance
		};

		// Act
		var result = _validator.Validate(worklog);

		// Assert
		result.IsValid.Should().BeTrue();
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
