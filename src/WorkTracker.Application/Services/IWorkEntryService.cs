using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Services;

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

	Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken);

	Task<Result<WorkEntry>> CreateWithOverlapResolutionAsync(string? ticketId, DateTime startTime, string? description, DateTime? endTime, OverlapResolutionPlan plan, CancellationToken cancellationToken);

	Task<Result<WorkEntry>> UpdateWithOverlapResolutionAsync(int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description, OverlapResolutionPlan plan, CancellationToken cancellationToken);
}
