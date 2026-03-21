using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Interfaces;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;

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
		var existingEntry = new WorkEntry
		{
			Id = 1,
			TicketId = "PROJ-100",
			StartTime = DateTime.Now.AddHours(-2),
			IsActive = true
		};
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
		var activeEntry = new WorkEntry
		{
			Id = 1,
			TicketId = "PROJ-123",
			StartTime = DateTime.Now.AddHours(-2),
			IsActive = true
		};
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
		var activeEntry = new WorkEntry
		{
			Id = 1,
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			IsActive = true
		};
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
		var activeEntry = new WorkEntry
		{
			Id = 1,
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			IsActive = true
		};
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
		var existingEntry = new WorkEntry
		{
			Id = 1,
			TicketId = "PROJ-123",
			StartTime = DateTime.Now.AddHours(-2),
			EndTime = DateTime.Now,
			IsActive = false
		};
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
		var existingEntry = new WorkEntry
		{
			Id = 1,
			TicketId = "PROJ-123",
			StartTime = DateTime.Now
		};
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
			new() { Id = 1, TicketId = "PROJ-123", StartTime = date.AddHours(9) },
			new() { Id = 2, TicketId = "PROJ-124", StartTime = date.AddHours(13) }
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
			new() { Id = 1, TicketId = "PROJ-123", StartTime = startDate },
			new() { Id = 2, TicketId = "PROJ-124", StartTime = startDate.AddDays(3) }
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
}