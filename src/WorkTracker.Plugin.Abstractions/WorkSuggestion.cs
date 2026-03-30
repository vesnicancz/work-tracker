namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Represents a work suggestion from an external source (calendar event, issue tracker, etc.)
/// </summary>
public class WorkSuggestion
{
	/// <summary>
	/// Display title for the suggestion (e.g., meeting subject, issue summary)
	/// </summary>
	public required string Title { get; init; }

	/// <summary>
	/// Ticket/Issue ID if applicable (e.g., "PROJ-123"). Null for non-ticket sources like calendar events.
	/// </summary>
	public string? TicketId { get; init; }

	/// <summary>
	/// Optional longer description
	/// </summary>
	public string? Description { get; init; }

	/// <summary>
	/// Suggested start time (e.g., calendar event start). Null for sources without times like Jira issues.
	/// </summary>
	public DateTime? StartTime { get; init; }

	/// <summary>
	/// Suggested end time (e.g., calendar event end). Null for sources without times like Jira issues.
	/// </summary>
	public DateTime? EndTime { get; init; }

	/// <summary>
	/// Name of the source plugin (e.g., "Jira", "Office 365 Calendar")
	/// </summary>
	public required string Source { get; init; }

	/// <summary>
	/// Unique identifier within the source (e.g., Jira issue key, calendar event ID).
	/// Used for deduplication across refreshes.
	/// </summary>
	public required string SourceId { get; init; }

	/// <summary>
	/// Optional URL to the source item (e.g., Jira issue link, calendar event link)
	/// </summary>
	public string? SourceUrl { get; init; }

	/// <summary>
	/// Additional metadata from the source
	/// </summary>
	public Dictionary<string, string>? Metadata { get; init; }
}
