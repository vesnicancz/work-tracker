using WorkTracker.Application.DTOs;

namespace WorkTracker.Application.Services;

public sealed class WorklogValidator : IWorklogValidator
{
	private const int MaxDurationMinutes = 24 * 60;

	/// <summary>
	/// Checks only what is wrong for every provider. Whether a ticket is required is up to each plugin:
	/// Tempo needs one, Goran G3 doesn't.
	/// <para>
	/// The duration is what gets logged and doesn't have to follow from the times: the submit dialog lets
	/// the user edit it, an aggregated row has no real end, and an entry started and stopped within one
	/// minute is logged as that minute with both times the same.
	/// </para>
	/// </summary>
	public ValidationResult Validate(WorklogDto worklog)
	{
		var errors = new List<string>();

		if (worklog.StartTime > worklog.EndTime)
		{
			errors.Add("End time cannot be before start time");
		}

		if (worklog.DurationMinutes <= 0)
		{
			errors.Add("Duration must be greater than 0");
		}

		if (worklog.DurationMinutes > MaxDurationMinutes)
		{
			errors.Add("Duration cannot exceed 24 hours");
		}

		return errors.Count != 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
	}
}
