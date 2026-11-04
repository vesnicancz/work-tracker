using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Infrastructure.Repositories;

/// <summary>
/// The queries behind <see cref="WorkEntryReader"/> and <see cref="WorkEntryRepository"/>. What comes back
/// isn't tracked: an entry that gets changed goes back through <c>UpdateAsync</c>.
/// </summary>
internal static class WorkEntryQueries
{
	public static Task<WorkEntry?> ActiveAsync(this IQueryable<WorkEntry> entries, CancellationToken cancellationToken)
	{
		return entries
			.AsNoTracking()
			.Where(e => e.IsActive)
			.OrderByDescending(e => e.StartTime)
			.FirstOrDefaultAsync(cancellationToken);
	}

	public static async Task<IEnumerable<WorkEntry>> OnDateAsync(this IQueryable<WorkEntry> entries, DateTime date, CancellationToken cancellationToken)
	{
		return await entries.InDaysAsync(date, date, cancellationToken);
	}

	public static async Task<IEnumerable<WorkEntry>> InDaysAsync(this IQueryable<WorkEntry> entries, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		var start = startDate.Date;
		var end = endDate.Date.AddDays(1);

		return await entries
			.AsNoTracking()
			.Where(e => e.StartTime >= start && e.StartTime < end)
			.OrderBy(e => e.StartTime)
			.ToListAsync(cancellationToken);
	}

	/// <summary>
	/// Builds the overlap detection query. Two time ranges overlap if: start1 &lt; end2 AND end1 &gt; start2.
	/// Null endTime is treated as ongoing (DateTime.MaxValue).
	/// </summary>
	public static IQueryable<WorkEntry> Overlapping(this IQueryable<WorkEntry> entries, int? excludeId, DateTime startTime, DateTime? endTime)
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
}
