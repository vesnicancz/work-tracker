using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Repositories;

namespace WorkTracker.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for <see cref="WorkEntryRepository"/> shared-context mode (used by UnitOfWork).
/// Key behavior: write operations (Add/Update/Delete) must NOT call SaveChanges — the UoW owns the commit.
/// </summary>
public class WorkEntryRepositorySharedContextTests : IDisposable
{
	private readonly DbContextOptions<WorkTrackerDbContext> _options;
	private readonly WorkTrackerDbContext _sharedContext;
	private readonly WorkEntryRepository _repository;

	public WorkEntryRepositorySharedContextTests()
	{
		_options = new DbContextOptionsBuilder<WorkTrackerDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.Options;

		_sharedContext = new WorkTrackerDbContext(_options);
		_repository = new WorkEntryRepository(_sharedContext);
	}

	public void Dispose()
	{
		_sharedContext.Database.EnsureDeleted();
		_sharedContext.Dispose();
	}

	[Fact]
	public async Task AddAsync_InSharedMode_DoesNotPersistUntilSaveChanges()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);

		// Act
		await _repository.AddAsync(entry, TestContext.Current.CancellationToken);

		// Assert — not yet persisted (visible from a separate context)
		using var verifyContext = new WorkTrackerDbContext(_options);
		var persisted = await verifyContext.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		persisted.Should().BeEmpty();

		// After explicit SaveChanges on shared context, it's persisted
		await _sharedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		using var verifyContextAfter = new WorkTrackerDbContext(_options);
		var persistedAfter = await verifyContextAfter.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		persistedAfter.Should().HaveCount(1);
	}

	[Fact]
	public async Task UpdateAsync_InSharedMode_DoesNotPersistUntilSaveChanges()
	{
		// Arrange — seed directly to DB
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);
		using (var seedContext = new WorkTrackerDbContext(_options))
		{
			seedContext.WorkEntries.Add(entry);
			await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Act — load via repository, update, but don't save
		var loaded = await _repository.GetByIdAsync(entry.Id, TestContext.Current.CancellationToken);
		loaded!.UpdateFields("UPDATED", null, null, null, DateTime.Now);
		await _repository.UpdateAsync(loaded, TestContext.Current.CancellationToken);

		// Assert — original value still in DB
		using var verifyContext = new WorkTrackerDbContext(_options);
		var fromDb = await verifyContext.WorkEntries.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
		fromDb.TicketId.Should().Be("PROJ-1");

		// After explicit SaveChanges
		await _sharedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		using var verifyContextAfter = new WorkTrackerDbContext(_options);
		var fromDbAfter = await verifyContextAfter.WorkEntries.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
		fromDbAfter.TicketId.Should().Be("UPDATED");
	}

	[Fact]
	public async Task DeleteAsync_InSharedMode_DoesNotPersistUntilSaveChanges()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);
		using (var seedContext = new WorkTrackerDbContext(_options))
		{
			seedContext.WorkEntries.Add(entry);
			await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Act
		await _repository.DeleteAsync(entry.Id, TestContext.Current.CancellationToken);

		// Assert — still in DB
		using var verifyContext = new WorkTrackerDbContext(_options);
		var stillThere = await verifyContext.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		stillThere.Should().HaveCount(1);

		// After SaveChanges — gone
		await _sharedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		using var verifyContextAfter = new WorkTrackerDbContext(_options);
		var afterSave = await verifyContextAfter.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		afterSave.Should().BeEmpty();
	}

	[Fact]
	public async Task ReadOperations_InSharedMode_Work()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), null, null, DateTime.Now);
		using (var seedContext = new WorkTrackerDbContext(_options))
		{
			seedContext.WorkEntries.Add(entry);
			await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Act & Assert
		var byId = await _repository.GetByIdAsync(entry.Id, TestContext.Current.CancellationToken);
		byId.Should().NotBeNull();

		var active = await _repository.GetActiveWorkEntryAsync(TestContext.Current.CancellationToken);
		active.Should().NotBeNull();
		active!.TicketId.Should().Be("PROJ-1");
	}
}
