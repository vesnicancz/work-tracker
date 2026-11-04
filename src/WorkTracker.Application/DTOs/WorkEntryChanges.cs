namespace WorkTracker.Application.DTOs;

/// <summary>
/// What to change on a work entry. A value left out stays as it is, which also means this can't clear
/// a value: for that the whole entry gets replaced with <c>UpdateWorkEntryAsync</c>.
/// </summary>
public sealed record WorkEntryChanges(
	string? TicketId = null,
	DateTime? StartTime = null,
	DateTime? EndTime = null,
	string? Description = null);
