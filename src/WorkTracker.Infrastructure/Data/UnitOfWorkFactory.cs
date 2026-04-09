using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Interfaces;

namespace WorkTracker.Infrastructure.Data;

public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
{
	private readonly IDbContextFactory<WorkTrackerDbContext> _contextFactory;
	private readonly ILogger<UnitOfWork> _logger;

	public UnitOfWorkFactory(IDbContextFactory<WorkTrackerDbContext> contextFactory, ILogger<UnitOfWork> logger)
	{
		_contextFactory = contextFactory;
		_logger = logger;
	}

	public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken)
		=> await UnitOfWork.CreateAsync(_contextFactory, _logger, cancellationToken);
}
