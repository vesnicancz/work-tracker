using FluentAssertions;
using WorkTracker.Application.Common;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;

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

	#region ToDomainWorklog Tests

	[Fact]
	public void ToDomainWorklog_WithValidPluginWorklog_ShouldMapAllProperties()
	{
		// Arrange
		var pluginWorklog = new PluginWorklogEntry
		{
			TicketId = "PROJ-456",
			Description = "Bug fix",
			StartTime = new DateTime(2025, 11, 2, 13, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 15, 30, 0),
			DurationMinutes = 150
		};

		// Act
		var result = pluginWorklog.ToDomainWorklog();

		// Assert
		result.Should().NotBeNull();
		result.TicketId.Should().Be(pluginWorklog.TicketId);
		result.Description.Should().Be(pluginWorklog.Description);
		result.StartTime.Should().Be(pluginWorklog.StartTime);
		result.EndTime.Should().Be(pluginWorklog.EndTime);
		result.DurationMinutes.Should().Be(pluginWorklog.DurationMinutes);
	}

	[Fact]
	public void ToDomainWorklog_RoundTrip_ShouldPreserveData()
	{
		// Arrange
		var original = new WorklogDto
		{
			TicketId = "PROJ-789",
			Description = "Testing",
			StartTime = new DateTime(2025, 11, 2, 10, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 12, 0, 0),
			DurationMinutes = 120
		};

		// Act - Convert to plugin and back
		var pluginWorklog = original.ToPluginWorklog();
		var result = pluginWorklog.ToDomainWorklog();

		// Assert
		result.Should().BeEquivalentTo(original);
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

	#region ToDomainWorklogs Tests

	[Fact]
	public void ToDomainWorklogs_WithMultiplePluginWorklogs_ShouldMapAll()
	{
		// Arrange
		var pluginWorklogs = new List<PluginWorklogEntry>
		{
			new()
			{
				TicketId = "PROJ-A",
				StartTime = DateTime.Now,
				EndTime = DateTime.Now.AddHours(1),
				DurationMinutes = 60
			},
			new()
			{
				TicketId = "PROJ-B",
				StartTime = DateTime.Now.AddHours(2),
				EndTime = DateTime.Now.AddHours(3),
				DurationMinutes = 60
			}
		};

		// Act
		var results = pluginWorklogs.ToDomainWorklogs().ToList();

		// Assert
		results.Should().HaveCount(2);
		results[0].TicketId.Should().Be("PROJ-A");
		results[1].TicketId.Should().Be("PROJ-B");
	}

	[Fact]
	public void ToDomainWorklogs_WithEmptyCollection_ShouldReturnEmpty()
	{
		// Arrange
		var pluginWorklogs = new List<PluginWorklogEntry>();

		// Act
		var results = pluginWorklogs.ToDomainWorklogs().ToList();

		// Assert
		results.Should().BeEmpty();
	}

	#endregion

	#region ToDomainResult Tests

	[Fact]
	public void ToDomainResult_WithSuccessfulPluginResult_ShouldReturnSuccess()
	{
		// Arrange
		var pluginResult = PluginResult<string>.Success("test value");

		// Act
		var result = pluginResult.ToDomainResult();

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().Be("test value");
		result.Error.Should().BeEmpty();
	}

	[Fact]
	public void ToDomainResult_WithFailedPluginResult_ShouldReturnFailure()
	{
		// Arrange
		var pluginResult = PluginResult<string>.Failure("error message");

		// Act
		var result = pluginResult.ToDomainResult();

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("error message");
		result.Value.Should().BeNull();
	}

	[Fact]
	public void ToDomainResult_WithNullError_ShouldUseDefaultMessage()
	{
		// Arrange
		var pluginResult = PluginResult<string>.Failure(null!);

		// Act
		var result = pluginResult.ToDomainResult();

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("Unknown error");
	}

	[Fact]
	public void ToDomainResult_WithComplexType_ShouldMapCorrectly()
	{
		// Arrange
		var complexObject = new { Id = 123, Name = "Test" };
		var pluginResult = PluginResult<object>.Success(complexObject);

		// Act
		var result = pluginResult.ToDomainResult();

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEquivalentTo(complexObject);
	}

	#endregion

	#region ToPluginResult Tests

	[Fact]
	public void ToPluginResult_WithSuccessfulResult_ShouldReturnSuccess()
	{
		// Arrange
		var domainResult = Result.Success("test value");

		// Act
		var result = domainResult.ToPluginResult();

		// Assert
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().Be("test value");
		result.Error.Should().BeNull();
	}

	[Fact]
	public void ToPluginResult_WithFailedResult_ShouldReturnFailure()
	{
		// Arrange
		var domainResult = Result.Failure<string>("error message");

		// Act
		var result = domainResult.ToPluginResult();

		// Assert
		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be("error message");
		result.Value.Should().BeNull();
	}

	[Fact]
	public void ToPluginResult_RoundTrip_ShouldPreserveData()
	{
		// Arrange
		var original = Result.Success(42);

		// Act - Convert to plugin and back
		var pluginResult = original.ToPluginResult();
		var result = pluginResult.ToDomainResult();

		// Assert
		result.IsSuccess.Should().Be(original.IsSuccess);
		result.Value.Should().Be(original.Value);
	}

	#endregion
}
