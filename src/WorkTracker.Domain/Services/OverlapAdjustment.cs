namespace WorkTracker.Domain.Services;

public sealed record OverlapAdjustment(
	int WorkEntryId,
	string? TicketId,
	string? Description,
	OverlapAdjustmentKind Kind,
	DateTime OriginalStart,
	DateTime? OriginalEnd,
	DateTime? NewStart,
	DateTime? NewEnd);