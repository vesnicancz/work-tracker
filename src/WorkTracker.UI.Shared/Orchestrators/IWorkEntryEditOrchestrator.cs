using WorkTracker.Application.Common;

namespace WorkTracker.UI.Shared.Orchestrators;

public interface IWorkEntryEditOrchestrator
{
	string? Validate(string? ticketId, string? description, bool hasEndTime, DateTime startDateTime, DateTime? endDateTime);

	Task<Result<bool>> SaveNewAsync(string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description, CancellationToken cancellationToken);

	Task<Result<bool>> SaveExistingAsync(int entryId, string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description, CancellationToken cancellationToken);
}
