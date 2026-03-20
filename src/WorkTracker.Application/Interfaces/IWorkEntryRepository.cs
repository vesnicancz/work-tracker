using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Interfaces;

public interface IWorkEntryRepository
{
	Task<WorkEntry?> GetByIdAsync(int id);

	Task<WorkEntry?> GetActiveWorkEntryAsync();

	Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date);

	Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

	Task<WorkEntry> AddAsync(WorkEntry workEntry);

	Task UpdateAsync(WorkEntry workEntry);

	Task DeleteAsync(int id);

	Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry);
}
