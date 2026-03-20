using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Services;

public interface IWorkEntryService
{
	Task<Result<WorkEntry>> StartWorkAsync(string? ticketId, DateTime? startTime = null, string? description = null, DateTime? endTime = null, CancellationToken cancellationToken = default);

	Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null, CancellationToken cancellationToken = default);

	Task<WorkEntry?> GetActiveWorkAsync(CancellationToken cancellationToken = default);

	Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date, CancellationToken cancellationToken = default);

	Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

	Task<Result<WorkEntry>> UpdateWorkEntryAsync(int id, string? ticketId = null, DateTime? startTime = null, DateTime? endTime = null, string? description = null, CancellationToken cancellationToken = default);

	Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken = default);
}
