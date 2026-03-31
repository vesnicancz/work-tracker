namespace WorkTracker.Domain.Entities;

public sealed class WorkEntry
{
	public int Id { get; init; }

	public string? TicketId { get; private set; }

	public DateTime StartTime { get; private set; }

	public DateTime? EndTime { get; private set; }

	public string? Description { get; private set; }

	public bool IsActive { get; private set; }

	public DateTime CreatedAt { get; init; }

	public DateTime? UpdatedAt { get; private set; }

	public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

	private WorkEntry()
	{
	}

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
	public void Stop(DateTime endTime, DateTime now) => AdjustEndTime(endTime, now);

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
	/// Adjusts only the start time, preserving all other fields.
	/// Times should be pre-rounded by the caller.
	/// </summary>
	public void AdjustStartTime(DateTime newStart, DateTime now)
	{
		StartTime = newStart;
		UpdatedAt = now;
	}

	/// <summary>
	/// Adjusts only the end time and marks the entry as inactive.
	/// Times should be pre-rounded by the caller.
	/// </summary>
	public void AdjustEndTime(DateTime newEnd, DateTime now)
	{
		EndTime = newEnd;
		IsActive = false;
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

	/// <summary>
	/// Reconstitutes a work entry from persistence. Use this to rebuild an entity with all fields including Id.
	/// </summary>
	internal static WorkEntry Reconstitute(int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description, bool isActive, DateTime createdAt, DateTime? updatedAt = null)
	{
		return new WorkEntry
		{
			Id = id,
			TicketId = ticketId,
			StartTime = startTime,
			EndTime = endTime,
			Description = description,
			IsActive = isActive,
			CreatedAt = createdAt,
			UpdatedAt = updatedAt
		};
	}
}