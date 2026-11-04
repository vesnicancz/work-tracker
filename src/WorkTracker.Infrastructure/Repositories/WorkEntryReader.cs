using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Repositories;

/// <summary>
/// Each read opens and closes its own <see cref="WorkTrackerDbContext"/>.
/// </summary>
public sealed class WorkEntryReader(IDbContextFactory<WorkTrackerDbContext> contextFactory) : IWorkEntryReader
{
	public async Task<WorkEntry?> GetByIdAsync(int id, CancellationToken cancellationToken)
	{
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
	}

	public async Task<WorkEntry?> GetActiveWorkEntryAsync(CancellationToken cancellationToken)
	{
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries.ActiveAsync(cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date, CancellationToken cancellationToken)
	{
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries.OnDateAsync(date, cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries.InDaysAsync(startDate, endDate, cancellationToken);
	}

	public async Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries
			.Overlapping(excludeEntryId, startTime, endTime)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}
}
