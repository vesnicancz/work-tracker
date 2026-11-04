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

	/// <summary>
	/// Builds the changes for starting work with no end at <paramref name="start"/>. Such an entry only
	/// claims its start instant, so entries planned for later (e.g. an upcoming meeting) don't block it:
	/// they are checked again once it gets stopped. Whatever is still running at that instant, the active
	/// entry as well as a meeting with a set end time, ends there. Nobody gets asked about this, so it
	/// takes no entry away the way a confirmed <see cref="Resolve"/> would.
	/// </summary>
	/// <param name="overlapping">Existing entries from <paramref name="start"/> on.</param>
	/// <param name="start">Rounded start time of the new entry.</param>
	/// <returns>The changes, or null when the work can't start there.</returns>
	public static IReadOnlyList<OverlapAdjustment>? ResolveOpenEndedStart(IReadOnlyList<WorkEntry> overlapping, DateTime start)
	{
		// Ending an entry at the new start only works for one that began before it. One starting at the
		// same minute would shrink to nothing, and an open-ended one starting later can't end before it began.
		if (overlapping.Any(e => e.StartTime >= start && (e.EndTime == null || e.StartTime == start)))
		{
			return null;
		}

		return overlapping
			.Where(e => e.StartTime < start)
			.Select(e => new OverlapAdjustment(e.Id, e.TicketId, e.Description, OverlapAdjustmentKind.TrimEnd, e.StartTime, e.EndTime, null, start))
			.ToList();
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
