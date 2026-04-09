namespace WorkTracker.Application.Interfaces;

/// <summary>
/// Creates <see cref="IUnitOfWork"/> instances. Each call opens a fresh transactional scope
/// (explicit DB transaction), so creation is asynchronous.
/// </summary>
public interface IUnitOfWorkFactory
{
	Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken);
}
