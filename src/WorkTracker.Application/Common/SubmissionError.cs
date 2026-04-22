namespace WorkTracker.Application.Common;

public class SubmissionError
{
	public string TicketId { get; set; } = string.Empty;

	public DateTime Date { get; set; }

	public string? Description { get; set; }

	public string ErrorMessage { get; set; } = string.Empty;

	/// <summary>
	/// In timed mode: "HH:mm-HH:mm" identifying the entry. Unused in aggregated mode
	/// (use <see cref="Description"/> for matching instead).
	/// </summary>
	public string? Details { get; set; }
}