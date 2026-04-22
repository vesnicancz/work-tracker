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

	/// <summary>
	/// Whether this suggestion represents an event that has already finished
	/// (only applicable for time-slotted items on the current day). Set by
	/// <see cref="SuggestionGroupViewModel"/> when items are populated.
	/// </summary>
	public bool IsPast { get; set; }

	public bool HasTimeSlot => StartTime.HasValue;

	public string TimeDisplay => HasTimeSlot
		? EndTime.HasValue ? $"{StartTime:HH:mm} – {EndTime:HH:mm}" : $"{StartTime:HH:mm}"
		: string.Empty;

	/// <summary>
	/// Left badge: time range for calendar events, ticket ID for issues.
	/// </summary>
	public string? BadgeText => HasTimeSlot ? TimeDisplay : TicketId;

	/// <summary>
	/// Whether badge should use accent color (ticket ID) vs muted (time).
	/// </summary>
	public bool IsBadgeAccent => TicketId != null && !HasTimeSlot;
}
