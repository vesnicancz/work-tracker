using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Repositories;
using WorkTracker.Infrastructure.Tests.Helpers;

namespace WorkTracker.Infrastructure.Tests.Services;

/// <summary>
/// Integration tests for <see cref="WorkEntryService"/> using real SQLite in-memory DB + real UnitOfWork.
/// These catch bugs that unit tests with mocked repositories miss — e.g. the auto-stop path bug
/// where tracked-but-unflushed changes are invisible to LINQ queries, causing false-positive overlaps.
/// SQLite is required here because EF InMemory provider doesn't support explicit transactions.
/// </summary>
public sealed class WorkEntryServiceIntegrationTests : IAsyncLifetime
{
	private SqliteInMemoryFixture _fixture = null!;
	private WorkEntryService _service = null!;

	public ValueTask InitializeAsync()
	{
		_fixture = new SqliteInMemoryFixture();

		var repository = new WorkEntryRepository(_fixture.ContextFactory);
		var uowFactory = new UnitOfWorkFactory(_fixture.ContextFactory, NullLogger<UnitOfWork>.Instance);
		_service = new WorkEntryService(
			repository,
			uowFactory,
			TimeProvider.System,
			NullLogger<WorkEntryService>.Instance);

		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await _fixture.DisposeAsync();
	}

	[Fact]
	public async Task StartWorkAsync_WithActiveEntry_AutoStopsAndStartsNewAtomically()
	{
		// Arrange — seed an active work entry directly in the DB
		var startedAt = DateTime.Now.AddHours(-2);
		var activeEntry = WorkEntry.Create("PROJ-OLD", startedAt, null, "Old work", startedAt);
		using (var seed = _fixture.CreateVerificationContext())
		{
			await seed.WorkEntries.AddAsync(activeEntry, TestContext.Current.CancellationToken);
			await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
		}
		var activeId = activeEntry.Id;

		// Act — start new work; auto-stop path must succeed.
		// Regression: before the fix, this returned OverlapError because the old entry's Stop()
		// was only in the EF change tracker and the overlap query still saw EndTime=null in DB.
		var result = await _service.StartWorkAsync("PROJ-NEW", null, "New work", cancellationToken: TestContext.Current.CancellationToken);

		// Assert — new entry created, old entry stopped, both persisted atomically
		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : "auto-stop should succeed");

		using var verify = _fixture.CreateVerificationContext();
		var all = await verify.WorkEntries.AsNoTracking().OrderBy(e => e.StartTime).ToListAsync(TestContext.Current.CancellationToken);
		all.Should().HaveCount(2);

		var oldEntry = all.Single(e => e.Id == activeId);
		oldEntry.IsActive.Should().BeFalse("auto-stop must have been committed");
		oldEntry.EndTime.Should().NotBeNull();

		var newEntry = all.Single(e => e.Id != activeId);
		newEntry.TicketId.Should().Be("PROJ-NEW");
		newEntry.IsActive.Should().BeTrue();
		newEntry.EndTime.Should().BeNull();
	}

	[Fact]
	public async Task StartWorkAsync_WithActiveEntry_InvalidNewEntry_RollsBackAutoStop()
	{
		// Arrange — active entry in DB
		var startedAt = DateTime.Now.AddHours(-2);
		var activeEntry = WorkEntry.Create("PROJ-OLD", startedAt, null, "Old work", startedAt);
		using (var seed = _fixture.CreateVerificationContext())
		{
			await seed.WorkEntries.AddAsync(activeEntry, TestContext.Current.CancellationToken);
			await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
		}
		var activeId = activeEntry.Id;

		// Act — pass null ticketId AND null description → new entry is invalid.
		// Auto-stop was already applied to the tracker; UoW dispose must roll it back via transaction rollback.
		var result = await _service.StartWorkAsync(null, null, null, cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		result.IsFailure.Should().BeTrue();

		using var verify = _fixture.CreateVerificationContext();
		var stillActive = await verify.WorkEntries.AsNoTracking().FirstAsync(e => e.Id == activeId, TestContext.Current.CancellationToken);
		stillActive.IsActive.Should().BeTrue("UoW rollback must undo the pending Stop()");
		stillActive.EndTime.Should().BeNull();
	}

	[Fact]
	public async Task CreateWithOverlapResolutionAsync_AtomicCommit_OnRealDb()
	{
		// Arrange — seed an existing entry that will be trimmed
		var existing = WorkEntry.Create("PROJ-EXISTING", new DateTime(2026, 1, 15, 8, 0, 0), new DateTime(2026, 1, 15, 11, 0, 0), "Long task", DateTime.Now);
		using (var seed = _fixture.CreateVerificationContext())
		{
			await seed.WorkEntries.AddAsync(existing, TestContext.Current.CancellationToken);
			await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		// Act — compute plan and create a new entry with overlap resolution
		var plan = await _service.ComputeOverlapResolutionAsync(null, new DateTime(2026, 1, 15, 10, 0, 0), new DateTime(2026, 1, 15, 12, 0, 0), TestContext.Current.CancellationToken);
		plan.HasAdjustments.Should().BeTrue();
		var result = await _service.CreateWithOverlapResolutionAsync("PROJ-NEW", new DateTime(2026, 1, 15, 10, 0, 0), "New task", new DateTime(2026, 1, 15, 12, 0, 0), plan, TestContext.Current.CancellationToken);

		// Assert — both changes committed
		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error : "resolution should succeed");

		using var verify = _fixture.CreateVerificationContext();
		var all = await verify.WorkEntries.AsNoTracking().OrderBy(e => e.StartTime).ToListAsync(TestContext.Current.CancellationToken);
		all.Should().HaveCount(2);
		all.Single(e => e.TicketId == "PROJ-EXISTING").EndTime.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0));
		all.Should().Contain(e => e.TicketId == "PROJ-NEW");
	}
}
