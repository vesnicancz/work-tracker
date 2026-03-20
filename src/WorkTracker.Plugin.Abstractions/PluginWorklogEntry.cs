namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Represents a worklog entry for plugin operations
/// </summary>
public class PluginWorklogEntry
{
	/// <summary>
	/// Ticket/Issue ID (e.g., "PROJ-123")
	/// </summary>
	public string? TicketId { get; set; }

	/// <summary>
	/// Work description
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Start time of the work
	/// </summary>
	public DateTime StartTime { get; set; }

	/// <summary>
	/// End time of the work
	/// </summary>
	public DateTime EndTime { get; set; }

	/// <summary>
	/// Duration in minutes
	/// </summary>
	public int DurationMinutes { get; set; }

	/// <summary>
	/// Category/type of work (optional)
	/// </summary>
	public string? Category { get; set; }

	/// <summary>
	/// Project name (optional)
	/// </summary>
	public string? ProjectName { get; set; }

	/// <summary>
	/// Additional metadata (optional)
	/// </summary>
	public Dictionary<string, string>? Metadata { get; set; }
}
