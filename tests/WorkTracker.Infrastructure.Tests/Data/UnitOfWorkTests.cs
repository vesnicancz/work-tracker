using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Tests.Helpers;

namespace WorkTracker.Infrastructure.Tests.Data;

/// <summary>
/// Integration tests for <see cref="UnitOfWork"/> — verify that multiple repository operations
/// are committed atomically via an explicit DB transaction and that dispose-without-save rolls back.
/// Uses SQLite in-memory because it supports real transactions (EF InMemory provider does not).
/// </summary>
public class UnitOfWorkTests : IAsyncLifetime
{
	private SqliteInMemoryFixture _fixture = null!;
	private UnitOfWorkFactory _factory = null!;

	public ValueTask InitializeAsync()
	{
		_fixture = new SqliteInMemoryFixture();
		_factory = new UnitOfWorkFactory(_fixture.ContextFactory, NullLogger<UnitOfWork>.Instance);
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await _fixture.DisposeAsync();
	}

	[Fact]
	public async Task SaveChangesAsync_PersistsAllOperations()
	{
		// Arrange
		await using var uow = await _factory.CreateAsync(TestContext.Current.CancellationToken);
		var entry1 = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);
		var entry2 = WorkEntry.Create("PROJ-2", new DateTime(2026, 1, 15, 11, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), null, DateTime.Now);

		// Act
		await uow.WorkEntries.AddAsync(entry1, TestContext.Current.CancellationToken);
		await uow.WorkEntries.AddAsync(entry2, TestContext.Current.CancellationToken);
		await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

		// Assert
		using var verify = _fixture.CreateVerificationContext();
		var persisted = await verify.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		persisted.Should().HaveCount(2);
		persisted.Should().Contain(e => e.TicketId == "PROJ-1");
		persisted.Should().Contain(e => e.TicketId == "PROJ-2");
	}

	[Fact]
	public async Task DisposeWithoutSave_RollsBackTransaction()
	{
		// Arrange
		var entry = WorkEntry.Create("PROJ-1", new DateTime(2026, 1, 15, 9, 0, 0), new DateTime(2026, 1, 15, 10, 0, 0), null, DateTime.Now);

		// Act — add then dispose without calling SaveChangesAsync → explicit rollback
		await using (var uow = await _factory.CreateAsync(TestContext.Current.CancellationToken))
		{
			await uow.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);
			// No SaveChangesAsync call — dispose triggers rollback
		}

		// Assert
		using var verify = _fixture.CreateVerificationContext();
		var persisted = await verify.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		persisted.Should().BeEmpty();
	}

	[Fact]
	public async Task MultipleOperations_CommitAtomically()
	{
		// Arrange — seed existing entries
		var seeded1 = WorkEntry.Create("SEED-1", new DateTime(2026, 1, 15, 8, 0, 0), new DateTime(2026, 1, 15, 9, 0, 0), null, DateTime.Now);
		var seeded2 = WorkEntry.Create("SEED-2", new DateTime(2026, 1, 15, 14, 0, 0), new DateTime(2026, 1, 15, 15, 0, 0), null, DateTime.Now);
		using (var seed = _fixture.CreateVerificationContext())
		{
			seed.WorkEntries.AddRange(seeded1, seeded2);
			await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
		}
		var seeded1Id = seeded1.Id;
		var seeded2Id = seeded2.Id;

		// Act — update one, delete another, add a new one — all in one transaction
		await using (var uow = await _factory.CreateAsync(TestContext.Current.CancellationToken))
		{
			var toUpdate = await uow.WorkEntries.GetByIdAsync(seeded1Id, TestContext.Current.CancellationToken);
			toUpdate!.UpdateFields("UPDATED", null, null, "Modified", DateTime.Now);
			await uow.WorkEntries.UpdateAsync(toUpdate, TestContext.Current.CancellationToken);

			await uow.WorkEntries.DeleteAsync(seeded2Id, TestContext.Current.CancellationToken);

			var newEntry = WorkEntry.Create("NEW", new DateTime(2026, 1, 15, 11, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), null, DateTime.Now);
			await uow.WorkEntries.AddAsync(newEntry, TestContext.Current.CancellationToken);

			await uow.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Assert
		using var verify = _fixture.CreateVerificationContext();
		var all = await verify.WorkEntries.AsNoTracking().OrderBy(e => e.StartTime).ToListAsync(TestContext.Current.CancellationToken);
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
		await using var uow = await _factory.CreateAsync(TestContext.Current.CancellationToken);
		await uow.WorkEntries.AddAsync(entry, TestContext.Current.CancellationToken);

		// Assert — not yet committed (verification context sees pre-transaction state)
		using (var outside = _fixture.CreateVerificationContext())
		{
			var visibleFromOutside = await outside.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
			visibleFromOutside.Should().BeEmpty();
		}

		// Commit and verify
		await uow.SaveChangesAsync(TestContext.Current.CancellationToken);
		using var afterCommit = _fixture.CreateVerificationContext();
		var visibleAfterCommit = await afterCommit.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		visibleAfterCommit.Should().HaveCount(1);
	}

	[Fact]
	public async Task WorkEntries_ReturnsSameRepositoryInstance()
	{
		// Arrange
		await using var uow = await _factory.CreateAsync(TestContext.Current.CancellationToken);

		// Act
		var repo1 = uow.WorkEntries;
		var repo2 = uow.WorkEntries;

		// Assert — single repository instance within the UoW scope
		repo1.Should().BeSameAs(repo2);
	}

	[Fact]
	public async Task PartialFailure_RollsBackEarlierOperations()
	{
		// Arrange — seed an entry that will be updated in the first operation
		var seeded = WorkEntry.Create("SEEDED", new DateTime(2026, 1, 15, 8, 0, 0), new DateTime(2026, 1, 15, 9, 0, 0), null, DateTime.Now);
		using (var seed = _fixture.CreateVerificationContext())
		{
			seed.WorkEntries.Add(seeded);
			await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Act — modify entry, add another, then bail out before SaveChanges → rollback
		await using (var uow = await _factory.CreateAsync(TestContext.Current.CancellationToken))
		{
			var toUpdate = await uow.WorkEntries.GetByIdAsync(seeded.Id, TestContext.Current.CancellationToken);
			toUpdate!.UpdateFields("MODIFIED", null, null, "changed", DateTime.Now);
			await uow.WorkEntries.UpdateAsync(toUpdate, TestContext.Current.CancellationToken);

			var newEntry = WorkEntry.Create("NEW", new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), null, DateTime.Now);
			await uow.WorkEntries.AddAsync(newEntry, TestContext.Current.CancellationToken);

			// Caller decides to bail — no SaveChangesAsync call → dispose rolls back
		}

		// Assert — original state preserved
		using var verify = _fixture.CreateVerificationContext();
		var all = await verify.WorkEntries.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
		all.Should().HaveCount(1);
		all[0].TicketId.Should().Be("SEEDED");
		all.Should().NotContain(e => e.TicketId == "NEW");
	}
}
