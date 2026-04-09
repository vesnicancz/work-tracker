using Microsoft.EntityFrameworkCore;
using WorkTracker.Application.Interfaces;

namespace WorkTracker.Infrastructure.Data;

public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
	private readonly IDbContextFactory<WorkTrackerDbContext> _contextFactory;

	public UnitOfWorkFactory(IDbContextFactory<WorkTrackerDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	public IUnitOfWork Create() => new UnitOfWork(_contextFactory);
}
