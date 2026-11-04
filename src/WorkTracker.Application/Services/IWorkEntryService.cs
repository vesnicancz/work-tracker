using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Domain.Entities;
using WorkTracker.Domain.Services;

namespace WorkTracker.Application.Services;

public interface IWorkEntryService
{
	Task<Result<WorkEntry>> StartWorkAsync(string? ticketId, DateTime? startTime = null, string? description = null, DateTime? endTime = null, CancellationToken cancellationToken = default);

	Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null, CancellationToken cancellationToken = default);

	Task<WorkEntry?> GetActiveWorkAsync(CancellationToken cancellationToken = default);

	Task<WorkEntry?> GetWorkEntryByIdAsync(int id, CancellationToken cancellationToken = default);

	Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date, CancellationToken cancellationToken = default);

	Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

	/// <summary>
	/// Replaces the whole entry with these values, as an edit dialog does: no end means it runs again,
	/// and no ticket or description clears them. To change only part of it use <see cref="EditWorkEntryAsync"/>.
	/// </summary>
	Task<Result<WorkEntry>> UpdateWorkEntryAsync(int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description, CancellationToken cancellationToken);

	/// <summary>
	/// Changes only what <paramref name="changes"/> holds, on the entry as it is stored at that moment.
	/// </summary>
	Task<Result<WorkEntry>> EditWorkEntryAsync(int id, WorkEntryChanges changes, CancellationToken cancellationToken);

	/// <summary>
	/// Moves the start of the work running at this moment, for work started late or too early. Fails
	/// rather than touching another entry when the new start would overlap one.
	/// </summary>
	Task<Result<WorkEntry>> ShiftActiveWorkStartAsync(TimeSpan delta, CancellationToken cancellationToken);

	Task<Result> DeleteWorkEntryAsync(int id, CancellationToken cancellationToken = default);

	Task<OverlapResolutionPlan> ComputeOverlapResolutionAsync(int? excludeEntryId, DateTime startTime, DateTime? endTime, CancellationToken cancellationToken);

	Task<Result<WorkEntry>> CreateWithOverlapResolutionAsync(string? ticketId, DateTime startTime, string? description, DateTime? endTime, OverlapResolutionPlan plan, CancellationToken cancellationToken);

	Task<Result<WorkEntry>> UpdateWithOverlapResolutionAsync(int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description, OverlapResolutionPlan plan, CancellationToken cancellationToken);
}
