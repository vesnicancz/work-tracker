namespace WorkTracker.Domain.DTOs;

/// <summary>
/// Data transfer object for worklog submission to external providers
/// Represents a single worklog entry without domain logic
/// </summary>
public class WorklogDto
{
	public string? TicketId { get; set; }

	public DateTime StartTime { get; set; }

	public DateTime EndTime { get; set; }

	public string? Description { get; set; }

	public int DurationMinutes { get; set; }
}
