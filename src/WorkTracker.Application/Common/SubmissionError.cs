namespace WorkTracker.Application.Common;

public class SubmissionError
{
	public string TicketId { get; set; } = string.Empty;

	public DateTime Date { get; set; }

	public string? Description { get; set; }

	public string ErrorMessage { get; set; } = string.Empty;

	/// <summary>
	/// Position of the failed worklog in the list that was submitted, or null when the plugin reported
	/// a worklog it wasn't handed.
	/// </summary>
	public int? WorklogIndex { get; set; }
}
