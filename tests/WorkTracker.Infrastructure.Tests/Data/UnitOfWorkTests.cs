using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Tests.Helpers;

namespace WorkTracker.Infrastructure.Tests.Data;

/// <summary>
/// Integration tests for <see cref="UnitOfWork"/> — verify that multiple repository operations
/// are committed atomically and that dispose-without-save discards pending changes.
/// </summary>
public class UnitOfWorkTests : IDisposable
{
	private readonly DbContextOptions<WorkTrackerDbContext> _options;
	private readonly TestDbContextFactory _contextFactory;
	private readonly WorkTrackerDbContext _verificationContext;

	public UnitOfWorkTests()
	{
		_options = new DbContextOptionsBuilder<WorkTrackerDbContext>()
			.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
			.Options;

		_contextFactory = new TestDbContextFactory(_options);
		_verificationContext = new WorkTrackerDbContext(_options);
	}

	public void Dispose()
	{
		_verificationContext.Database.EnsureDeleted();
		_verificationContext.Dispose();
	}

	[Fact]
	public async Task SaveChangesAsync_PersistsAllOperations()
	{
		// Arrange
		await using var uow = new UnitOfWork(_contextFactory);
		var entry1 = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);
		var entry2 = WorkEntry.Create("PROJ-2", new DateTime(2026, 1, 15, 11, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), null, DateTime.Now);

		// Act
		await uow.WorkEntries.AddAsync(entry1, TestContext.Current.CancellationToken);
		await uow.WorkEntries.AddAsync(entry2, TestContext.Current.CancellationToken);
		await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Assert
		var persisted = await _verificationContext.WorkEntries.ToListAsync(TestContext.Current.CancellationToken);
		persisted.Should().HaveCount(2);
		persisted.Should().Contain(e => e.TicketId == "PROJ-1");
		persisted.Should().Contain(e => e.TicketId == "PROJ-2");
	}

	[Fact]
	public async Task DisposeWithoutSave_DiscardsAllPendingChanges()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);

		// Act — add then dispose without calling SaveChangesAsync
		await using (var uow = new UnitOfWork(_contextFactory))
		{
			await uow.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
			// No SaveChangesAsync call — dispose should discard changes
		}

		// Assert
		var persisted = await _verificationContext.WorkEntries.ToListAsync(TestContext.Current.CancellationToken);
		persisted.Should().BeEmpty();
	}

	[Fact]
	public async Task MultipleOperations_CommitAtomically()
	{
		// Arrange — seed an existing entry to update + delete
		var seeded1 = WorkEntry.Create("SEED-1", new DateTime(2026, 1, 15, 8, 0, 0), new DateTime(2026, 1, 15, 9, 0, 0), null, DateTime.Now);
		var seeded2 = WorkEntry.Create("SEED-2", new DateTime(2026, 1, 15, 14, 0, 0), new DateTime(2026, 1, 15, 15, 0, 0), null, DateTime.Now);
		await _verificationContext.WorkEntries.AddRangeAsync([seeded1, seeded2], TestContext.Current.CancellationToken);
		await _verificationContext.SaveChangesAsync(TestContext.Current.CancellationToken);
		var seeded1Id = seeded1.Id;
		var seeded2Id = seeded2.Id;

		// Act — update one, delete another, add a new one — all in one transaction
		await using (var uow = new UnitOfWork(_contextFactory))
		{
			var toUpdate = await uow.WorkEntries.GetByIdAsync(seeded1Id, TestContext.Current.CancellationToken);
			toUpdate!.UpdateFields("UPDATED", null, null, "Modified", DateTime.Now);
			await uow.WorkEntries.UpdateAsync(toUpdate, TestContext.Current.CancellationToken);

			await uow.WorkEntries.DeleteAsync(seeded2Id, TestContext.Current.CancellationToken);

			var newEntry = WorkEntry.Create("NEW", new DateTime(2026, 1, 15, 11, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), null, DateTime.Now);
			await uow.WorkEntries.AddAsync(newEntry, TestContext.Current.CancellationToken);

			await uow.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Assert — use a fresh context to bypass caching
		using var freshContext = new WorkTrackerDbContext(_options);
		var all = await freshContext.WorkEntries.AsNoTracking().OrderBy(e => e.StartTime).ToListAsync(TestContext.Current.CancellationToken);
		all.Should().HaveCount(2);
		all.Should().Contain(e => e.TicketId == "UPDATED");
		all.Should().Contain(e => e.TicketId == "NEW");
		all.Should().NotContain(e => e.TicketId == "SEED-2");
	}

	[Fact]
	public async Task OperationsBeforeSaveChanges_NotVisibleToOtherContexts()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);

		// Act
		await using var uow = new UnitOfWork(_contextFactory);
		await uow.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);

		// Assert — not yet persisted from another context's point of view
		using var outsideContext = new WorkTrackerDbContext(_options);
		var visibleFromOutside = await outsideContext.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		visibleFromOutside.Should().BeEmpty();

		// Commit and verify
		await uow.SaveChangesAsync(TestContext.Current.CancellationToken);
		using var afterCommitContext = new WorkTrackerDbContext(_options);
		var visibleAfterCommit = await afterCommitContext.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		visibleAfterCommit.Should().HaveCount(1);
	}

	[Fact]
	public async Task WorkEntries_ReturnsSameRepositoryInstance()
	{
		// Arrange
		await using var uow = new UnitOfWork(_contextFactory);

		// Act
		var repo1 = uow.WorkEntries;
		var repo2 = uow.WorkEntries;

		// Assert — single repository instance within the UoW scope
		repo1.Should().BeSameAs(repo2);
	}
}
