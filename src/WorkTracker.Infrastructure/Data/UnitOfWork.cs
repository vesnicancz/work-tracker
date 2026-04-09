using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Repositories;

namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// Provides a transactional scope sharing a single <see cref="WorkTrackerDbContext"/> and an explicit
/// <see cref="IDbContextTransaction"/> across all repository operations. Changes are committed atomically
/// when <see cref="SaveChangesAsync"/> is called. Disposing without committing rolls the transaction back.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
	private readonly WorkTrackerDbContext _context;
	private readonly IDbContextTransaction _transaction;
	private readonly ILogger<UnitOfWork> _logger;
	private bool _committed;

	private UnitOfWork(WorkTrackerDbContext context, IDbContextTransaction transaction, ILogger<UnitOfWork> logger)
	{
		_context = context;
		_transaction = transaction;
		_logger = logger;
		WorkEntries = new WorkEntryRepository(_context);
	}

	public static async Task<UnitOfWork> CreateAsync(IDbContextFactory<WorkTrackerDbContext> contextFactory, ILogger<UnitOfWork> logger, CancellationToken cancellationToken)
	{
		var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		try
		{
			var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
			return new UnitOfWork(context, transaction, logger);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to begin Unit of Work transaction");
			await context.DisposeAsync();
			throw;
		}
	}

	public IWorkEntryRepository WorkEntries { get; }

	public async Task SaveChangesAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _context.SaveChangesAsync(cancellationToken);
			await _transaction.CommitAsync(cancellationToken);
			_committed = true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to commit Unit of Work — the transaction will be rolled back on dispose");
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (!_committed)
		{
			try
			{
				await _transaction.RollbackAsync();
				_logger.LogDebug("Unit of Work disposed without commit — transaction rolled back");
			}
			catch (Exception ex)
			{
				// The transaction may already be in a terminal state (e.g. after a failed
				// SaveChangesAsync which auto-rolls back, or a dropped connection). We still need
				// to dispose the context to release the connection — log but don't rethrow.
				_logger.LogWarning(ex, "Failed to roll back Unit of Work transaction on dispose");
			}
		}

		await _transaction.DisposeAsync();
		await _context.DisposeAsync();
	}
}
