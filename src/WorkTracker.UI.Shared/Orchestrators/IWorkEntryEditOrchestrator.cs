using WorkTracker.Application.Common;

namespace WorkTracker.UI.Shared.Orchestrators;

public interface IWorkEntryEditOrchestrator
{
	string? Validate(string? ticketId, string? description, bool hasEndTime, DateTime startDateTime, DateTime? endDateTime);

	Task<Result> SaveNewAsync(string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description);

	Task<Result> SaveExistingAsync(int entryId, string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description);
}