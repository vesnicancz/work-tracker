using WorkTracker.Domain.Interfaces;

namespace WorkTracker.Application.Interfaces;

/// <summary>
/// Provides a transactional scope for coordinating multiple repository operations.
/// All changes made through the exposed repositories are committed atomically via <see cref="SaveChangesAsync"/>.
/// If disposed without calling <see cref="SaveChangesAsync"/>, all pending changes are discarded.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
	IWorkEntryRepository WorkEntries { get; }

	Task SaveChangesAsync(CancellationToken cancellationToken);
}
