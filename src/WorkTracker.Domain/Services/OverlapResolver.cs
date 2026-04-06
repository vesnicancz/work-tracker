using WorkTracker.Domain.Entities;

namespace WorkTracker.Domain.Services;

/// <summary>
/// Pure domain service that determines how overlapping work entries should be adjusted
/// when a new or updated entry conflicts with existing ones.
/// </summary>
public static class OverlapResolver
{
	private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Builds a resolution plan for all overlapping entries against a candidate time range.
	/// </summary>
	/// <param name="overlapping">Existing entries that overlap with the candidate.</param>
	/// <param name="candidateStart">Rounded start time of the candidate entry.</param>
	/// <param name="candidateEnd">
	/// Rounded end time of the candidate entry, or <see cref="DateTime.MaxValue"/> for active (ongoing) entries.
	/// </param>
	public static IReadOnlyList<OverlapAdjustment> Resolve(
		IReadOnlyList<WorkEntry> overlapping, DateTime candidateStart, DateTime candidateEnd)
	{
		var adjustments = new List<OverlapAdjustment>(overlapping.Count);

		foreach (var existing in overlapping)
		{
			var existingEnd = existing.EndTime ?? DateTime.MaxValue;
			adjustments.Add(DetermineAdjustment(existing, existingEnd, candidateStart, candidateEnd));
		}

		return adjustments;
	}

	internal static OverlapAdjustment DetermineAdjustment(
		WorkEntry existing, DateTime existingEnd, DateTime candidateStart, DateTime candidateEnd)
	{
		OverlapAdjustment Adj(OverlapAdjustmentKind kind, DateTime? newStart = null, DateTime? newEnd = null) =>
			new(existing.Id, existing.TicketId, existing.Description, kind,
				existing.StartTime, existing.EndTime, newStart, newEnd);

		// Complete cover: candidate fully contains existing
		if (candidateStart <= existing.StartTime && candidateEnd >= existingEnd)
		{
			return Adj(OverlapAdjustmentKind.Delete);
		}

		// Split: candidate is inside existing (only for completed entries).
		// Active entries (no EndTime) are never split — they are trimmed instead,
		// because splitting would create a new active entry after the candidate.
		if (candidateStart > existing.StartTime && candidateEnd < existingEnd && existing.EndTime.HasValue)
		{
			var firstHalf = candidateStart - existing.StartTime;
			var secondHalf = existingEnd - candidateEnd;

			if (firstHalf < MinDuration && secondHalf < MinDuration)
			{
				return Adj(OverlapAdjustmentKind.Delete);
			}

			if (firstHalf < MinDuration)
			{
				return Adj(OverlapAdjustmentKind.TrimStart, newStart: candidateEnd);
			}

			if (secondHalf < MinDuration)
			{
				return Adj(OverlapAdjustmentKind.TrimEnd, newEnd: candidateStart);
			}

			return Adj(OverlapAdjustmentKind.Split, newStart: candidateEnd, newEnd: candidateStart);
		}

		// Head overlap: candidate covers the end of existing
		if (candidateStart > existing.StartTime)
		{
			var remaining = candidateStart - existing.StartTime;
			if (remaining < MinDuration)
			{
				return Adj(OverlapAdjustmentKind.Delete);
			}

			return Adj(OverlapAdjustmentKind.TrimEnd, newEnd: candidateStart);
		}

		// Tail overlap: candidate covers the beginning of existing
		{
			var remaining = existingEnd - candidateEnd;
			if (remaining < MinDuration)
			{
				return Adj(OverlapAdjustmentKind.Delete);
			}

			return Adj(OverlapAdjustmentKind.TrimStart, newStart: candidateEnd);
		}
	}
}
