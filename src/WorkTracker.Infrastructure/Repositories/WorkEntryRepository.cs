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
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await BuildOverlapQuery(context.WorkEntries, workEntry.Id, workEntry.StartTime, workEntry.EndTime)
			.AnyAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		return await BuildOverlapQuery(context.WorkEntries, excludeEntryId ?? 0, startTime, endTime)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	/// <summary>
	/// Builds the overlap detection query. Two time ranges overlap if: start1 &lt; end2 AND end1 &gt; start2.
	/// Null endTime is treated as ongoing (DateTime.MaxValue).
	/// </summary>
	private static IQueryable<WorkEntry> BuildOverlapQuery(IQueryable<WorkEntry> entries, int excludeId, DateTime startTime, DateTime? endTime)
	{
		var effectiveEnd = endTime ?? DateTime.MaxValue;
		return entries
			.AsNoTracking()
			.Where(e => e.Id != excludeId &&
						e.StartTime < effectiveEnd &&
						(e.EndTime == null || e.EndTime > startTime));
	}
}
