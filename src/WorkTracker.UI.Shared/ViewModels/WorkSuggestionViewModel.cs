namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// ViewModel for a single work suggestion item
/// </summary>
public class WorkSuggestionViewModel
{
	public required string Title { get; init; }
	public string? TicketId { get; init; }
	public string? Description { get; init; }
	public DateTime? StartTime { get; init; }
	public DateTime? EndTime { get; init; }
	public required string Source { get; init; }
	public required string SourceId { get; init; }
	public string? SourceUrl { get; init; }

	public bool HasTimeSlot => StartTime.HasValue;

	public string TimeDisplay => HasTimeSlot
		? $"{StartTime:HH:mm} – {EndTime:HH:mm}"
		: string.Empty;

	public string DisplayText => TicketId != null
		? $"{TicketId}: {Title}"
		: Title;
}
