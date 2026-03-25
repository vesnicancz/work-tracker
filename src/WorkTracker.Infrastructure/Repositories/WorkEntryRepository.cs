using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Repositories;

public sealed class WorkEntryRepository : IWorkEntryRepository
{
	private readonly IDbContextFactory<WorkTrackerDbContext> _contextFactory;

	public WorkEntryRepository(IDbContextFactory<WorkTrackerDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	public async Task<WorkEntry?> GetByIdAsync(int id, CancellationToken cancellationToken)
	{
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries.FindAsync([id], cancellationToken);
	}

	public async Task<WorkEntry?> GetActiveWorkEntryAsync(CancellationToken cancellationToken)
	{
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.IsActive)
			.OrderByDescending(e => e.StartTime)
			.FirstOrDefaultAsync(cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date, CancellationToken cancellationToken)
	{
		var startOfDay = date.Date;
		var endOfDay = startOfDay.AddDays(1);

		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.StartTime >= startOfDay && e.StartTime < endOfDay)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		var start = startDate.Date;
		var end = endDate.Date.AddDays(1);

		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.StartTime >= start && e.StartTime < end)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	public async Task<WorkEntry> AddAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		context.WorkEntries.Add(workEntry);
		await context.SaveChangesAsync(cancellationToken);
		return workEntry;
	}

	public async Task UpdateAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		context.WorkEntries.Update(workEntry);
		await context.SaveChangesAsync(cancellationToken);
	}

	public async Task DeleteAsync(int id, CancellationToken cancellationToken)
	{
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		var workEntry = await context.WorkEntries.FindAsync([id], cancellationToken);
		if (workEntry != null)
		{
			context.WorkEntries.Remove(workEntry);
			await context.SaveChangesAsync(cancellationToken);
		}
	}

	public async Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		// Determine the effective end time for this entry (null means ongoing/infinite)
		var entryEnd = workEntry.EndTime ?? DateTime.MaxValue;

		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		// Two time ranges overlap if: start1 < end2 AND end1 > start2
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.Id != workEntry.Id &&
						e.StartTime < entryEnd &&
						(e.EndTime == null || e.EndTime > workEntry.StartTime))
			.AnyAsync(cancellationToken);
	}
}
