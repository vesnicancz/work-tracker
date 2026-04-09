using WorkTracker.Domain.Entities;

namespace WorkTracker.Domain.Interfaces;

public interface IWorkEntryRepository
{
	Task<WorkEntry?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

	Task<WorkEntry?> GetActiveWorkEntryAsync(CancellationToken cancellationToken = default);

	Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default);

	Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

	Task<WorkEntry> AddAsync(WorkEntry workEntry, CancellationToken cancellationToken = default);

	Task UpdateAsync(WorkEntry workEntry, CancellationToken cancellationToken = default);

	Task DeleteAsync(int id, CancellationToken cancellationToken = default);

	Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lightweight existence check for overlapping entries. Uses EF's <c>AnyAsync</c> — produces
	/// a <c>SELECT EXISTS</c>-style query without materializing matching rows. Prefer this over
	/// <see cref="GetOverlappingEntriesAsync"/> when only existence is needed.
	/// </summary>
	Task<bool> HasOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken = default);
}
