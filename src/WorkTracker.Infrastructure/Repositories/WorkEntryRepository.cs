using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Repositories;

/// <summary>
/// Works on the <see cref="WorkTrackerDbContext"/> of a <see cref="UnitOfWork"/>, which owns its lifetime
/// and the commit.
/// </summary>
public sealed class WorkEntryRepository : IWorkEntryRepository
{
	private readonly WorkTrackerDbContext _context;

	internal WorkEntryRepository(WorkTrackerDbContext context)
	{
		_context = context;
	}

	public async Task<WorkEntry?> GetByIdAsync(int id, CancellationToken cancellationToken)
	{
		return await _context.WorkEntries.FindAsync([id], cancellationToken);
	}

	public Task<WorkEntry?> GetActiveWorkEntryAsync(CancellationToken cancellationToken)
	{
		return _context.WorkEntries.ActiveAsync(cancellationToken);
	}

	public Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date, CancellationToken cancellationToken)
	{
		return _context.WorkEntries.OnDateAsync(date, cancellationToken);
	}

	public Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		return _context.WorkEntries.InDaysAsync(startDate, endDate, cancellationToken);
	}

	public async Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		return await _context.WorkEntries
			.Overlapping(excludeEntryId, startTime, endTime)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	public Task<WorkEntry> AddAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		_context.WorkEntries.Add(workEntry);
		return Task.FromResult(workEntry);
	}

	public Task UpdateAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		_context.WorkEntries.Update(workEntry);
		return Task.CompletedTask;
	}

	public async Task DeleteAsync(int id, CancellationToken cancellationToken)
	{
		var workEntry = await _context.WorkEntries.FindAsync([id], cancellationToken);
		if (workEntry != null)
		{
			_context.WorkEntries.Remove(workEntry);
		}
	}

	public Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		return _context.WorkEntries
			.Overlapping(workEntry.Id, workEntry.StartTime, workEntry.EndTime)
			.AnyAsync(cancellationToken);
	}

	public Task<bool> HasOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		return _context.WorkEntries
			.Overlapping(excludeEntryId, startTime, endTime)
			.AnyAsync(cancellationToken);
	}
}
