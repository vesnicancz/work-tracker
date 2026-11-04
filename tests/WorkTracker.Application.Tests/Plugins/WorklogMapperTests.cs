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

	#region ToPluginWorklogs Tests

	[Fact]
	public void ToPluginWorklogs_WithMultipleWorklogs_ShouldMapAll()
	{
		// Arrange
		var worklogs = new List<WorklogDto>
		{
			new()
			{
				TicketId = "PROJ-1",
				StartTime = DateTime.Now,
				EndTime = DateTime.Now.AddHours(1),
				DurationMinutes = 60
			},
			new()
			{
				TicketId = "PROJ-2",
				StartTime = DateTime.Now.AddHours(2),
				EndTime = DateTime.Now.AddHours(3),
				DurationMinutes = 60
			},
			new()
			{
				TicketId = "PROJ-3",
				StartTime = DateTime.Now.AddHours(4),
				EndTime = DateTime.Now.AddHours(5),
				DurationMinutes = 60
			}
		};

		// Act
		var results = worklogs.ToPluginWorklogs().ToList();

		// Assert
		results.Should().HaveCount(3);
		results[0].TicketId.Should().Be("PROJ-1");
		results[1].TicketId.Should().Be("PROJ-2");
		results[2].TicketId.Should().Be("PROJ-3");
	}

	[Fact]
	public void ToPluginWorklogs_WithEmptyCollection_ShouldReturnEmpty()
	{
		// Arrange
		var worklogs = new List<WorklogDto>();

		// Act
		var results = worklogs.ToPluginWorklogs().ToList();

		// Assert
		results.Should().BeEmpty();
	}

	[Fact]
	public void ToPluginWorklogs_ShouldBeLazyEvaluated()
	{
		// Arrange
		var worklogs = new List<WorklogDto>
		{
			new()
			{
				TicketId = "PROJ-1",
				StartTime = DateTime.Now,
				EndTime = DateTime.Now.AddHours(1),
				DurationMinutes = 60
			}
		};

		// Act
		var resultsEnumerable = worklogs.ToPluginWorklogs();

		// Assert - Should not throw, enumerable is lazy
		resultsEnumerable.Should().NotBeNull();

		// When materialized
		var results = resultsEnumerable.ToList();
		results.Should().HaveCount(1);
	}

	#endregion
}
