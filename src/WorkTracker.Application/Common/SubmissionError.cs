namespace WorkTracker.Application.Common;

public class SubmissionError
{
	public string TicketId { get; set; } = string.Empty;

	public DateTime Date { get; set; }

	public string ErrorMessage { get; set; } = string.Empty;

	public string? Details { get; set; }
}