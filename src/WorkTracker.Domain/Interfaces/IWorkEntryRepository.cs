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

	Task<IReadOnlyList<WorkEntry>> GetOverlappingEntriesAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken = default);
}
