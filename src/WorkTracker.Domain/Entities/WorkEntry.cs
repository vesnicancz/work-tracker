namespace WorkTracker.Domain.Entities;

public class WorkEntry
{
	public int Id { get; set; }

	public string? TicketId { get; set; }

	public DateTime StartTime { get; set; }

	public DateTime? EndTime { get; set; }

	public string? Description { get; set; }

	public bool IsActive { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime? UpdatedAt { get; set; }

	public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

	public bool IsValid()
	{
		// At least one of TicketId or Description must be provided
		if (string.IsNullOrWhiteSpace(TicketId) && string.IsNullOrWhiteSpace(Description))
		{
			return false;
		}

		if (EndTime.HasValue && EndTime.Value < StartTime)
		{
			return false;
		}

		return true;
	}
}