using FluentAssertions;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Plugins;

namespace WorkTracker.Application.Tests.Plugins;

public class WorklogMapperTests
{
	#region ToPluginWorklog Tests

	[Fact]
	public void ToPluginWorklog_WithValidWorklog_ShouldMapAllProperties()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			Description = "Working on feature",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 11, 0, 0),
			DurationMinutes = 120
		};

		// Act
		var result = worklog.ToPluginWorklog();

		// Assert
		result.Should().NotBeNull();
		result.TicketId.Should().Be(worklog.TicketId);
		result.Description.Should().Be(worklog.Description);
		result.StartTime.Should().Be(worklog.StartTime);
		result.EndTime.Should().Be(worklog.EndTime);
		result.DurationMinutes.Should().Be(worklog.DurationMinutes);
	}

	[Fact]
	public void ToPluginWorklog_WithNullTicketId_ShouldMapCorrectly()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = null,
			Description = "General work",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1),
			DurationMinutes = 60
		};

		// Act
		var result = worklog.ToPluginWorklog();

		// Assert
		result.TicketId.Should().BeNull();
		result.Description.Should().Be(worklog.Description);
	}

	[Fact]
	public void ToPluginWorklog_WithNullDescription_ShouldMapCorrectly()
	{
		// Arrange
		var worklog = new WorklogDto
		{
			TicketId = "PROJ-123",
			Description = null,
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1),
			DurationMinutes = 60
		};

		// Act
		var result = worklog.ToPluginWorklog();

		// Assert
		result.TicketId.Should().Be(worklog.TicketId);
		result.Description.Should().BeNull();
	}

	#endregion
}
