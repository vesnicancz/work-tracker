using Microsoft.EntityFrameworkCore;
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Entities;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Repositories;

public class WorkEntryRepository : IWorkEntryRepository
{
	private readonly IDbContextFactory<WorkTrackerDbContext> _contextFactory;

	public WorkEntryRepository(IDbContextFactory<WorkTrackerDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	public async Task<WorkEntry?> GetByIdAsync(int id)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		return await context.WorkEntries.FindAsync(id);
	}

	public async Task<WorkEntry?> GetActiveWorkEntryAsync()
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.IsActive)
			.OrderByDescending(e => e.StartTime)
			.FirstOrDefaultAsync();
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date)
	{
		var startOfDay = date.Date;
		var endOfDay = startOfDay.AddDays(1);

		await using var context = await _contextFactory.CreateDbContextAsync();
		// Note: Using AsNoTracking for read-only queries is a best practice
		// It prevents memory overhead from change tracking and ensures fresh data
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.StartTime >= startOfDay && e.StartTime < endOfDay)
			.OrderBy(e => e.StartTime)
			.ToListAsync();
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
	{
		var start = startDate.Date;
		var end = endDate.Date.AddDays(1);

		await using var context = await _contextFactory.CreateDbContextAsync();
		// Note: Using AsNoTracking for read-only queries is a best practice
		// It prevents memory overhead from change tracking and ensures fresh data
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.StartTime >= start && e.StartTime < end)
			.OrderBy(e => e.StartTime)
			.ToListAsync();
	}

	public async Task<WorkEntry> AddAsync(WorkEntry workEntry)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		context.WorkEntries.Add(workEntry);
		await context.SaveChangesAsync();
		return workEntry;
	}

	public async Task UpdateAsync(WorkEntry workEntry)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		context.WorkEntries.Update(workEntry);
		await context.SaveChangesAsync();
	}

	public async Task DeleteAsync(int id)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		var workEntry = await context.WorkEntries.FindAsync(id);
		if (workEntry != null)
		{
			context.WorkEntries.Remove(workEntry);
			await context.SaveChangesAsync();
		}
	}

	public async Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry)
	{
		// Determine the effective end time for this entry (null means ongoing/infinite)
		var entryEnd = workEntry.EndTime ?? DateTime.MaxValue;

		await using var context = await _contextFactory.CreateDbContextAsync();
		// Check for overlaps using SQL query instead of loading all entries into memory
		// Two time ranges overlap if: start1 < end2 AND end1 > start2
		return await context.WorkEntries
			.AsNoTracking()
			.Where(e => e.Id != workEntry.Id &&
						e.StartTime < entryEnd &&
						(e.EndTime == null || e.EndTime > workEntry.StartTime))
			.AnyAsync();
	}
}
