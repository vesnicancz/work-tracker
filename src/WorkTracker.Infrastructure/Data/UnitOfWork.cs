using Microsoft.EntityFrameworkCore;
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Repositories;

namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// Provides a transactional scope sharing a single DbContext across all repository operations.
/// Changes are committed atomically when <see cref="SaveChangesAsync"/> is called.
/// Disposing without saving discards all pending changes.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
	private readonly WorkTrackerDbContext _context;

	public UnitOfWork(IDbContextFactory<WorkTrackerDbContext> contextFactory)
	{
		_context = contextFactory.CreateDbContext();
		WorkEntries = new WorkEntryRepository(_context);
	}

	public IWorkEntryRepository WorkEntries { get; }

	public Task SaveChangesAsync(CancellationToken cancellationToken)
		=> _context.SaveChangesAsync(cancellationToken);

	public async ValueTask DisposeAsync()
		=> await _context.DisposeAsync();
}
