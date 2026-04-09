namespace WorkTracker.Application.Interfaces;

/// <summary>
/// Creates <see cref="IUnitOfWork"/> instances. Each call returns a fresh transactional scope.
/// </summary>
public interface IUnitOfWorkFactory
{
	IUnitOfWork Create();
}
