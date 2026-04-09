using WorkTracker.Domain.Interfaces;

namespace WorkTracker.Application.Interfaces;

/// <summary>
/// Provides a transactional scope for coordinating multiple repository operations.
/// All changes made through the exposed repositories are committed atomically via <see cref="SaveChangesAsync"/>,
/// which commits the underlying explicit DB transaction.
/// If disposed without calling <see cref="SaveChangesAsync"/>, the transaction is rolled back.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
	IWorkEntryRepository WorkEntries { get; }

	Task SaveChangesAsync(CancellationToken cancellationToken);
}
