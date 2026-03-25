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

	/// <summary>
	/// Stops this work entry by setting end time and marking as inactive.
	/// Times should be pre-rounded by the caller.
	/// </summary>
	public void Stop(DateTime endTime, DateTime now)
	{
		EndTime = endTime;
		IsActive = false;
		UpdatedAt = now;
	}

	/// <summary>
	/// Updates mutable fields of this work entry.
	/// StartTime is only updated when provided (non-null). Times should be pre-rounded by the caller.
	/// </summary>
	public void UpdateFields(string? ticketId, DateTime? startTime, DateTime? endTime, string? description, DateTime now)
	{
		TicketId = ticketId;
		if (startTime.HasValue)
		{
			StartTime = startTime.Value;
		}

		EndTime = endTime;
		IsActive = !endTime.HasValue;
		Description = description;
		UpdatedAt = now;
	}

	/// <summary>
	/// Creates a new work entry. Times should be pre-rounded by the caller.
	/// </summary>
	public static WorkEntry Create(string? ticketId, DateTime startTime, DateTime? endTime, string? description, DateTime now)
	{
		return new WorkEntry
		{
			TicketId = ticketId,
			StartTime = startTime,
			EndTime = endTime,
			Description = description,
			IsActive = !endTime.HasValue,
			CreatedAt = now
		};
	}
}