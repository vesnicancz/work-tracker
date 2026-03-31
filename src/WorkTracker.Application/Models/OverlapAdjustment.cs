namespace WorkTracker.Application.Models;

public enum OverlapAdjustmentKind
{
	TrimEnd,
	TrimStart,
	Delete,
	Split
}

public sealed record OverlapAdjustment(
	int WorkEntryId,
	string? TicketId,
	string? Description,
	OverlapAdjustmentKind Kind,
	DateTime OriginalStart,
	DateTime? OriginalEnd,
	DateTime? NewStart,
	DateTime? NewEnd);
