using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Repositories;

namespace WorkTracker.Infrastructure.Tests.Repositories;

public class WorkEntryRepositoryTests : IDisposable
{
	private readonly DbContextOptions<WorkTrackerDbContext> _options;
	private readonly TestDbContextFactory _contextFactory;
	private readonly WorkEntryRepository _repository;
	private readonly WorkTrackerDbContext _testContext;

	public WorkEntryRepositoryTests()
	{
		_options = new DbContextOptionsBuilder<WorkTrackerDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.Options;

		_contextFactory = new TestDbContextFactory(_options);
		_repository = new WorkEntryRepository(_contextFactory);

		// Keep a context for test data setup
		_testContext = new WorkTrackerDbContext(_options);
	}

	public void Dispose()
	{
		_testContext.Database.EnsureDeleted();
		_testContext.Dispose();
	}

	#region GetByIdAsync Tests

	[Fact]
	public async Task GetByIdAsync_WithExistingId_ShouldReturnWorkEntry()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1)
		};
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var result = await _repository.GetByIdAsync(entry.Id, TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result!.Id.Should().Be(entry.Id);
		result.TicketId.Should().Be("PROJ-123");
	}

	[Fact]
	public async Task GetByIdAsync_WithNonExistentId_ShouldReturnNull()
	{
		// Act
		var result = await _repository.GetByIdAsync(999, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeNull();
	}

	#endregion

	#region GetActiveWorkEntryAsync Tests

	[Fact]
	public async Task GetActiveWorkEntryAsync_WithActiveEntry_ShouldReturnIt()
	{
		// Arrange
		var activeEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			IsActive = true
		};
		await _testContext.WorkEntries.AddAsync(activeEntry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var result = await _repository.GetActiveWorkEntryAsync(TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result!.Id.Should().Be(activeEntry.Id);
		result.IsActive.Should().BeTrue();
	}

	[Fact]
	public async Task GetActiveWorkEntryAsync_WithMultipleActiveEntries_ShouldReturnMostRecent()
	{
		// Arrange
		var olderEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now.AddHours(-2),
			IsActive = true
		};
		var newerEntry = new WorkEntry
		{
			TicketId = "PROJ-124",
			StartTime = DateTime.Now,
			IsActive = true
		};
		await _testContext.WorkEntries.AddRangeAsync([olderEntry, newerEntry], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var result = await _repository.GetActiveWorkEntryAsync(TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result!.Id.Should().Be(newerEntry.Id);
		result.TicketId.Should().Be("PROJ-124");
	}

	[Fact]
	public async Task GetActiveWorkEntryAsync_WithNoActiveEntries_ShouldReturnNull()
	{
		// Arrange
		var inactiveEntry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now.AddHours(-1),
			EndTime = DateTime.Now,
			IsActive = false
		};
		await _testContext.WorkEntries.AddAsync(inactiveEntry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var result = await _repository.GetActiveWorkEntryAsync(TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeNull();
	}

	#endregion

	#region GetByDateAsync Tests

	[Fact]
	public async Task GetByDateAsync_WithEntriesOnDate_ShouldReturnThem()
	{
		// Arrange
		var targetDate = new DateTime(2025, 11, 2);
		var entry1 = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = targetDate.AddHours(9),
			EndTime = targetDate.AddHours(10)
		};
		var entry2 = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = targetDate.AddHours(14),
			EndTime = targetDate.AddHours(16)
		};
		await _testContext.WorkEntries.AddRangeAsync([entry1, entry2], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = await _repository.GetByDateAsync(targetDate, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCount(2);
		results.Should().Contain(e => e.TicketId == "PROJ-1");
		results.Should().Contain(e => e.TicketId == "PROJ-2");
	}

	[Fact]
	public async Task GetByDateAsync_ShouldOrderByStartTime()
	{
		// Arrange
		var targetDate = new DateTime(2025, 11, 2);
		var laterEntry = new WorkEntry
		{
			TicketId = "PROJ-LATER",
			StartTime = targetDate.AddHours(14)
		};
		var earlierEntry = new WorkEntry
		{
			TicketId = "PROJ-EARLIER",
			StartTime = targetDate.AddHours(9)
		};
		await _testContext.WorkEntries.AddRangeAsync([laterEntry, earlierEntry], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = (await _repository.GetByDateAsync(targetDate, TestContext.Current.CancellationToken)).ToList();

		// Assert
		results.Should().HaveCount(2);
		results[0].TicketId.Should().Be("PROJ-EARLIER");
		results[1].TicketId.Should().Be("PROJ-LATER");
	}

	[Fact]
	public async Task GetByDateAsync_WithNoEntriesOnDate_ShouldReturnEmpty()
	{
		// Arrange
		var targetDate = new DateTime(2025, 11, 2);
		var differentDate = new DateTime(2025, 11, 3);
		var entry = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = differentDate.AddHours(9)
		};
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = await _repository.GetByDateAsync(targetDate, TestContext.Current.CancellationToken);

		// Assert
		results.Should().BeEmpty();
	}

	[Fact]
	public async Task GetByDateAsync_ShouldExcludeEntriesFromNextDay()
	{
		// Arrange
		var targetDate = new DateTime(2025, 11, 2);
		var onDate = new WorkEntry
		{
			TicketId = "PROJ-ON-DATE",
			StartTime = targetDate.AddHours(23)
		};
		var nextDay = new WorkEntry
		{
			TicketId = "PROJ-NEXT-DAY",
			StartTime = targetDate.AddDays(1).AddHours(1)
		};
		await _testContext.WorkEntries.AddRangeAsync([onDate, nextDay], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = await _repository.GetByDateAsync(targetDate, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCount(1);
		results.Should().Contain(e => e.TicketId == "PROJ-ON-DATE");
		results.Should().NotContain(e => e.TicketId == "PROJ-NEXT-DAY");
	}

	#endregion

	#region GetByDateRangeAsync Tests

	[Fact]
	public async Task GetByDateRangeAsync_WithEntriesInRange_ShouldReturnThem()
	{
		// Arrange
		var startDate = new DateTime(2025, 11, 1);
		var endDate = new DateTime(2025, 11, 7);

		var entry1 = new WorkEntry { TicketId = "PROJ-1", StartTime = startDate.AddHours(9) };
		var entry2 = new WorkEntry { TicketId = "PROJ-2", StartTime = startDate.AddDays(3).AddHours(9) };
		var entry3 = new WorkEntry { TicketId = "PROJ-3", StartTime = endDate.AddHours(9) };

		await _testContext.WorkEntries.AddRangeAsync([entry1, entry2, entry3], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = await _repository.GetByDateRangeAsync(startDate, endDate, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCount(3);
	}

	[Fact]
	public async Task GetByDateRangeAsync_ShouldOrderByStartTime()
	{
		// Arrange
		var startDate = new DateTime(2025, 11, 1);
		var endDate = new DateTime(2025, 11, 7);

		var entry3 = new WorkEntry { TicketId = "PROJ-3", StartTime = endDate.AddHours(9) };
		var entry1 = new WorkEntry { TicketId = "PROJ-1", StartTime = startDate.AddHours(9) };
		var entry2 = new WorkEntry { TicketId = "PROJ-2", StartTime = startDate.AddDays(3).AddHours(9) };

		await _testContext.WorkEntries.AddRangeAsync([entry3, entry1, entry2], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = (await _repository.GetByDateRangeAsync(startDate, endDate, TestContext.Current.CancellationToken)).ToList();

		// Assert
		results.Should().HaveCount(3);
		results[0].TicketId.Should().Be("PROJ-1");
		results[1].TicketId.Should().Be("PROJ-2");
		results[2].TicketId.Should().Be("PROJ-3");
	}

	[Fact]
	public async Task GetByDateRangeAsync_ShouldExcludeEntriesOutsideRange()
	{
		// Arrange
		var startDate = new DateTime(2025, 11, 1);
		var endDate = new DateTime(2025, 11, 7);

		var before = new WorkEntry { TicketId = "PROJ-BEFORE", StartTime = startDate.AddDays(-1) };
		var inRange = new WorkEntry { TicketId = "PROJ-IN-RANGE", StartTime = startDate.AddDays(3) };
		var after = new WorkEntry { TicketId = "PROJ-AFTER", StartTime = endDate.AddDays(1) };

		await _testContext.WorkEntries.AddRangeAsync([before, inRange, after], TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = await _repository.GetByDateRangeAsync(startDate, endDate, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCount(1);
		results.Should().Contain(e => e.TicketId == "PROJ-IN-RANGE");
	}

	[Fact]
	public async Task GetByDateRangeAsync_WithSingleDay_ShouldReturnEntriesFromThatDay()
	{
		// Arrange
		var date = new DateTime(2025, 11, 2);
		var entry = new WorkEntry { TicketId = "PROJ-1", StartTime = date.AddHours(9) };
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act
		var results = await _repository.GetByDateRangeAsync(date, date, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCount(1);
		results.Should().Contain(e => e.TicketId == "PROJ-1");
	}

	#endregion

	#region AddAsync Tests

	[Fact]
	public async Task AddAsync_ShouldAddWorkEntry()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			Description = "Test work"
		};

		// Act
		var result = await _repository.AddAsync(entry, TestContext.Current.CancellationToken);

		// Assert
		result.Should().NotBeNull();
		result.Id.Should().BeGreaterThan(0);
		result.TicketId.Should().Be("PROJ-123");

		var saved = await _testContext.WorkEntries.FindAsync([result.Id], TestContext.Current.CancellationToken);
		saved.Should().NotBeNull();
	}

	[Fact]
	public async Task AddAsync_ShouldPersistToDatabase()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			CreatedAt = DateTime.Now
		};

		// Act
		await _repository.AddAsync(entry, TestContext.Current.CancellationToken);

		// Assert
		var saved = await _testContext.WorkEntries.FindAsync([entry.Id], TestContext.Current.CancellationToken);
		saved.Should().NotBeNull();
		saved!.TicketId.Should().Be("PROJ-123");
	}

	#endregion

	#region UpdateAsync Tests

	[Fact]
	public async Task UpdateAsync_ShouldUpdateWorkEntry()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now
		};
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		_testContext.Entry(entry).State = EntityState.Detached;

		// Modify
		entry.TicketId = "PROJ-456";
		entry.Description = "Updated description";

		// Act
		await _repository.UpdateAsync(entry, TestContext.Current.CancellationToken);

		// Assert
		var updated = await _testContext.WorkEntries.FindAsync([entry.Id], TestContext.Current.CancellationToken);
		updated.Should().NotBeNull();
		updated!.TicketId.Should().Be("PROJ-456");
		updated.Description.Should().Be("Updated description");
	}

	[Fact]
	public async Task UpdateAsync_ShouldPersistChanges()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now,
			CreatedAt = DateTime.Now
		};
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		_testContext.Entry(entry).State = EntityState.Detached;

		// Modify
		entry.Description = "Updated";
		entry.TicketId = "PROJ-456";

		// Act
		await _repository.UpdateAsync(entry, TestContext.Current.CancellationToken);

		// Assert
		var updated = await _testContext.WorkEntries.FindAsync([entry.Id], TestContext.Current.CancellationToken);
		updated.Should().NotBeNull();
		updated!.Description.Should().Be("Updated");
		updated.TicketId.Should().Be("PROJ-456");
	}

	#endregion

	#region DeleteAsync Tests

	[Fact]
	public async Task DeleteAsync_WithExistingEntry_ShouldRemoveIt()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-123",
			StartTime = DateTime.Now
		};
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		var entryId = entry.Id;

		// Act
		await _repository.DeleteAsync(entryId, TestContext.Current.CancellationToken);

		// Assert - Use repository to verify deletion (repository uses factory)
		var deleted = await _repository.GetByIdAsync(entryId, TestContext.Current.CancellationToken);
		deleted.Should().BeNull();
	}

	[Fact]
	public async Task DeleteAsync_WithNonExistentId_ShouldNotThrow()
	{
		// Act & Assert
		var act = async () => await _repository.DeleteAsync(999, TestContext.Current.CancellationToken);
		await act.Should().NotThrowAsync();
	}

	#endregion

	#region HasOverlappingEntriesAsync Tests

	[Fact]
	public async Task HasOverlappingEntriesAsync_WithNoOverlap_ShouldReturnFalse()
	{
		// Arrange
		var existing = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 11, 0, 0)
		};
		await _testContext.WorkEntries.AddAsync(existing, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		var newEntry = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = new DateTime(2025, 11, 2, 13, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 15, 0, 0)
		};

		// Act
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(newEntry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeFalse();
	}

	[Fact]
	public async Task HasOverlappingEntriesAsync_WithCompleteOverlap_ShouldReturnTrue()
	{
		// Arrange
		var existing = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 17, 0, 0)
		};
		await _testContext.WorkEntries.AddAsync(existing, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		var newEntry = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = new DateTime(2025, 11, 2, 10, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 12, 0, 0)
		};

		// Act
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(newEntry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeTrue();
	}

	[Fact]
	public async Task HasOverlappingEntriesAsync_WithPartialOverlap_ShouldReturnTrue()
	{
		// Arrange
		var existing = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 12, 0, 0)
		};
		await _testContext.WorkEntries.AddAsync(existing, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		var newEntry = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = new DateTime(2025, 11, 2, 11, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 14, 0, 0)
		};

		// Act
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(newEntry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeTrue();
	}

	[Fact]
	public async Task HasOverlappingEntriesAsync_WithActiveEntry_ShouldReturnTrue()
	{
		// Arrange
		var activeEntry = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = null,
			IsActive = true
		};
		await _testContext.WorkEntries.AddAsync(activeEntry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		var newEntry = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = new DateTime(2025, 11, 2, 10, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 12, 0, 0)
		};

		// Act
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(newEntry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeTrue();
	}

	[Fact]
	public async Task HasOverlappingEntriesAsync_WithAdjacentEntries_ShouldReturnFalse()
	{
		// Arrange
		var existing = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 11, 0, 0)
		};
		await _testContext.WorkEntries.AddAsync(existing, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		var newEntry = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = new DateTime(2025, 11, 2, 11, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 13, 0, 0)
		};

		// Act
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(newEntry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeFalse();
	}

	[Fact]
	public async Task HasOverlappingEntriesAsync_ShouldIgnoreSameEntry()
	{
		// Arrange
		var entry = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 11, 0, 0)
		};
		await _testContext.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Act - Check the same entry against itself
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(entry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeFalse();
	}

	[Fact]
	public async Task HasOverlappingEntriesAsync_WithNewActiveEntry_ShouldCheckAgainstExisting()
	{
		// Arrange
		var existing = new WorkEntry
		{
			TicketId = "PROJ-1",
			StartTime = new DateTime(2025, 11, 2, 9, 0, 0),
			EndTime = new DateTime(2025, 11, 2, 11, 0, 0)
		};
		await _testContext.WorkEntries.AddAsync(existing, TestContext.Current.CancellationToken);
		await _testContext.SaveChangesAsync(TestContext.Current.CancellationToken);

		var newActiveEntry = new WorkEntry
		{
			TicketId = "PROJ-2",
			StartTime = new DateTime(2025, 11, 2, 10, 0, 0),
			EndTime = null,
			IsActive = true
		};

		// Act
		var hasOverlap = await _repository.HasOverlappingEntriesAsync(newActiveEntry, TestContext.Current.CancellationToken);

		// Assert
		hasOverlap.Should().BeTrue();
	}

	#endregion

	/// <summary>
	/// Test implementation of IDbContextFactory for in-memory testing
	/// </summary>
	private class TestDbContextFactory : IDbContextFactory<WorkTrackerDbContext>
	{
		private readonly DbContextOptions<WorkTrackerDbContext> _options;

		public TestDbContextFactory(DbContextOptions<WorkTrackerDbContext> options)
		{
			_options = options;
		}

		public WorkTrackerDbContext CreateDbContext()
		{
			return new WorkTrackerDbContext(_options);
		}

		public async Task<WorkTrackerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
		{
			return await Task.FromResult(CreateDbContext());
		}
	}
}
