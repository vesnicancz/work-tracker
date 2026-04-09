using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Repositories;

public sealed class WorkEntryRepository : IWorkEntryRepository
{
	private readonly IDbContextFactory<WorkTrackerDbContext>? _contextFactory;
	private readonly WorkTrackerDbContext? _sharedContext;

	/// <summary>
	/// Factory mode: each operation creates and disposes its own DbContext.
	/// Used for standalone (non-transactional) repository usage.
	/// </summary>
	public WorkEntryRepository(IDbContextFactory<WorkTrackerDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	/// <summary>
	/// Shared context mode: all operations use the provided DbContext without calling SaveChanges.
	/// Used within a <see cref="UnitOfWork"/> for transactional coordination.
	/// </summary>
	internal WorkEntryRepository(WorkTrackerDbContext sharedContext)
	{
		_sharedContext = sharedContext;
	}

	private async Task<ContextScope> GetContextAsync(CancellationToken cancellationToken)
	{
		if (_sharedContext != null)
		{
			return new ContextScope(_sharedContext, ownsContext: false);
		}

		var context = await _contextFactory!.CreateDbContextAsync(cancellationToken);
		return new ContextScope(context, ownsContext: true);
	}

	public async Task<WorkEntry?> GetByIdAsync(int id, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		return await scope.Context.WorkEntries.FindAsync([id], cancellationToken);
	}

	public async Task<WorkEntry?> GetActiveWorkEntryAsync(CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		return await scope.Context.WorkEntries
			.AsNoTracking()
			.Where(e => e.IsActive)
			.OrderByDescending(e => e.StartTime)
			.FirstOrDefaultAsync(cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date, CancellationToken cancellationToken)
	{
		var startOfDay = date.Date;
		var endOfDay = startOfDay.AddDays(1);

		await using var scope = await GetContextAsync(cancellationToken);
		return await scope.Context.WorkEntries
			.AsNoTracking()
			.Where(e => e.StartTime >= startOfDay && e.StartTime < endOfDay)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	public async Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		var start = startDate.Date;
		var end = endDate.Date.AddDays(1);

		await using var scope = await GetContextAsync(cancellationToken);
		return await scope.Context.WorkEntries
			.AsNoTracking()
			.Where(e => e.StartTime >= start && e.StartTime < end)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	public async Task<WorkEntry> AddAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		scope.Context.WorkEntries.Add(workEntry);

		if (scope.OwnsContext)
		{
			await scope.Context.SaveChangesAsync(cancellationToken);
		}

		return workEntry;
	}

	public async Task UpdateAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		scope.Context.WorkEntries.Update(workEntry);

		if (scope.OwnsContext)
		{
			await scope.Context.SaveChangesAsync(cancellationToken);
		}
	}

	public async Task DeleteAsync(int id, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		var workEntry = await scope.Context.WorkEntries.FindAsync([id], cancellationToken);
		if (workEntry != null)
		{
			scope.Context.WorkEntries.Remove(workEntry);

			if (scope.OwnsContext)
			{
				await scope.Context.SaveChangesAsync(cancellationToken);
			}
		}
	}

	public async Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		return await BuildOverlapQuery(scope.Context.WorkEntries, workEntry.Id, workEntry.StartTime, workEntry.EndTime)
			.AnyAsync(cancellationToken);
	}

	public async Task<bool> HasOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		return await BuildOverlapQuery(scope.Context.WorkEntries, excludeEntryId, startTime, endTime)
			.AnyAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken)
	{
		await using var scope = await GetContextAsync(cancellationToken);
		return await BuildOverlapQuery(scope.Context.WorkEntries, excludeEntryId, startTime, endTime)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	/// <summary>
	/// Builds the overlap detection query. Two time ranges overlap if: start1 &lt; end2 AND end1 &gt; start2.
	/// Null endTime is treated as ongoing (DateTime.MaxValue).
	/// </summary>
	private static IQueryable<WorkEntry> BuildOverlapQuery(IQueryable<WorkEntry> entries, int? excludeId, DateTime startTime, DateTime? endTime)
	{
		var effectiveEnd = endTime ?? DateTime.MaxValue;
		var query = entries
			.AsNoTracking()
			.Where(e => e.StartTime < effectiveEnd &&
						(e.EndTime == null || e.EndTime > startTime));

		if (excludeId.HasValue)
		{
			query = query.Where(e => e.Id != excludeId.Value);
		}

		return query;
	}

	/// <summary>
	/// Manages a DbContext lifetime — disposes it only in factory mode (ownsContext: true).
	/// In shared mode (UnitOfWork), the context is owned by the UoW and not disposed here.
	/// </summary>
	internal readonly struct ContextScope : IAsyncDisposable
	{
		public WorkTrackerDbContext Context { get; }
		public bool OwnsContext { get; }

		public ContextScope(WorkTrackerDbContext context, bool ownsContext)
		{
			Context = context;
			OwnsContext = ownsContext;
		}

		public ValueTask DisposeAsync()
			=> OwnsContext ? Context.DisposeAsync() : ValueTask.CompletedTask;
	}
}
