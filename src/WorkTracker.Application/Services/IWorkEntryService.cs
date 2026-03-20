using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Services;

public interface IWorkEntryService
{
	Task<Result<WorkEntry>> StartWorkAsync(string? ticketId, DateTime? startTime = null, string? description = null, DateTime? endTime = null);

	Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null);

	Task<WorkEntry?> GetActiveWorkAsync();

	Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date);

	Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate);

	Task<Result<WorkEntry>> UpdateWorkEntryAsync(int id, string? ticketId = null, DateTime? startTime = null, DateTime? endTime = null, string? description = null);

	Task<Result> DeleteWorkEntryAsync(int id);
}