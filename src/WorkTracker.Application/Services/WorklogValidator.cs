using WorkTracker.Application.DTOs;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Services;

public sealed class WorklogValidator : IWorklogValidator
{
	public const double MinDurationSeconds = 1;
	public const double MaxDurationHours = 24;
	public const int MaxDurationMinutes = (int)(MaxDurationHours * 60);

	public ValidationResult ValidateForSubmission(WorkEntry entry)
	{
		if (entry == null)
		{
			return ValidationResult.Failure("Work entry cannot be null");
		}

		if (string.IsNullOrWhiteSpace(entry.TicketId))
		{
			return ValidationResult.Failure("Ticket ID is required");
		}

		if (!entry.EndTime.HasValue)
		{
			return ValidationResult.Failure($"Work entry for {entry.TicketId} is still active and cannot be submitted");
		}

		if (entry.StartTime >= entry.EndTime.Value)
		{
			return ValidationResult.Failure($"Start time must be before end time for {entry.TicketId}");
		}

		var duration = entry.Duration;
		if (!duration.HasValue || duration.Value.TotalSeconds <= 0)
		{
			return ValidationResult.Failure($"Invalid duration for {entry.TicketId}");
		}

		if (duration.Value.TotalSeconds < MinDurationSeconds)
		{
			return ValidationResult.Failure($"Duration must be at least {MinDurationSeconds} second(s) for {entry.TicketId}");
		}

		if (duration.Value.TotalHours > MaxDurationHours)
		{
			return ValidationResult.Failure($"Duration cannot exceed {MaxDurationHours} hours for {entry.TicketId}");
		}

		return ValidationResult.Success();
	}

	public ValidationResult ValidateMultiple(IEnumerable<WorkEntry> entries)
	{
		var entriesList = entries.ToList();

		if (!entriesList.Any())
		{
			return ValidationResult.Failure("No work entries provided");
		}

		var errors = new List<string>();

		foreach (var entry in entriesList)
		{
			var validationResult = ValidateForSubmission(entry);
			if (!validationResult.IsValid)
			{
				errors.AddRange(validationResult.Errors);
			}
		}

		if (errors.Any())
		{
			return ValidationResult.Failure(errors.ToArray());
		}

		return ValidationResult.Success();
	}

	public ValidationResult Validate(WorklogDto worklog)
	{
		var errors = new List<string>();

		if (worklog == null)
		{
			return ValidationResult.Failure("Worklog cannot be null");
		}

		if (worklog.StartTime >= worklog.EndTime)
		{
			errors.Add($"Start time must be before end time");
		}

		if (worklog.DurationMinutes <= 0)
		{
			errors.Add($"Duration must be greater than 0");
		}

		if (worklog.DurationMinutes > MaxDurationMinutes)
		{
			errors.Add($"Duration cannot exceed 24 hours");
		}

		// Validate that duration matches the time difference
		var calculatedDuration = (int)(worklog.EndTime - worklog.StartTime).TotalMinutes;
		if (Math.Abs(calculatedDuration - worklog.DurationMinutes) > 1)
		{
			errors.Add($"Duration mismatch: expected {calculatedDuration} minutes but got {worklog.DurationMinutes} minutes");
		}

		if (errors.Any())
		{
			return ValidationResult.Failure(errors.ToArray());
		}

		return ValidationResult.Success();
	}
}
