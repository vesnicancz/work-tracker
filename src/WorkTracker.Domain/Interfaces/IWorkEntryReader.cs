using WorkTracker.Domain.Entities;

namespace WorkTracker.Domain.Interfaces;

/// <summary>
/// Reads work entries as they are stored at that moment. Anything that decides on a write reads through
/// the <see cref="IWorkEntryRepository"/> of its unit of work instead, where nothing can change in between.
/// </summary>
public interface IWorkEntryReader
{
	Task<WorkEntry?> GetByIdAsync(int id, CancellationToken cancellationToken);

	Task<WorkEntry?> GetActiveWorkEntryAsync(CancellationToken cancellationToken);

	Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date, CancellationToken cancellationToken);

	Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

	Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken);
}
