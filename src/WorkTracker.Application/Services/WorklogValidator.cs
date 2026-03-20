using WorkTracker.Application.Common;
using WorkTracker.Domain.DTOs;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Services;

public class WorklogValidator : IWorklogValidator
{
	public Result ValidateForSubmission(WorkEntry entry)
	{
		if (entry == null)
		{
			return Result.Failure("Work entry cannot be null");
		}

		if (string.IsNullOrWhiteSpace(entry.TicketId))
		{
			return Result.Failure("Ticket ID is required");
		}

		if (!entry.EndTime.HasValue)
		{
			return Result.Failure($"Work entry for {entry.TicketId} is still active and cannot be submitted");
		}

		if (entry.StartTime >= entry.EndTime.Value)
		{
			return Result.Failure($"Start time must be before end time for {entry.TicketId}");
		}

		var duration = entry.Duration;
		if (!duration.HasValue || duration.Value.TotalSeconds <= 0)
		{
			return Result.Failure($"Invalid duration for {entry.TicketId}");
		}

		// Tempo has a minimum worklog duration (typically 1 second)
		if (duration.Value.TotalSeconds < 1)
		{
			return Result.Failure($"Duration must be at least 1 second for {entry.TicketId}");
		}

		// Check for reasonable maximum duration (e.g., 24 hours)
		if (duration.Value.TotalHours > 24)
		{
			return Result.Failure($"Duration cannot exceed 24 hours for {entry.TicketId}");
		}

		return Result.Success();
	}

	public Result ValidateMultiple(IEnumerable<WorkEntry> entries)
	{
		var entriesList = entries.ToList();

		if (!entriesList.Any())
		{
			return Result.Failure("No work entries provided");
		}

		var errors = new List<string>();

		foreach (var entry in entriesList)
		{
			var validationResult = ValidateForSubmission(entry);
			if (validationResult.IsFailure)
			{
				errors.Add(validationResult.Error);
			}
		}

		if (errors.Any())
		{
			return Result.Failure(string.Join("; ", errors));
		}

		return Result.Success();
	}

	public ValidationResult Validate(WorklogDto worklog)
	{
		var errors = new List<string>();

		if (worklog == null)
		{
			return ValidationResult.Failure("Worklog cannot be null");
		}

		// Ticket ID is optional for some providers
		// if (string.IsNullOrWhiteSpace(worklog.TicketId))
		// {
		//     errors.Add("Ticket ID is required");
		// }

		if (worklog.StartTime >= worklog.EndTime)
		{
			errors.Add($"Start time must be before end time");
		}

		if (worklog.DurationMinutes <= 0)
		{
			errors.Add($"Duration must be greater than 0");
		}

		// Check for reasonable maximum duration (e.g., 24 hours = 1440 minutes)
		if (worklog.DurationMinutes > 1440)
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