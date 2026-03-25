using WorkTracker.Application.DTOs;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Application.Services;

/// <summary>
/// Service for validating worklogs before submission
/// </summary>
public interface IWorklogValidator
{
	/// <summary>
	/// Validates a work entry for submission
	/// </summary>
	ValidationResult ValidateForSubmission(WorkEntry entry);

	/// <summary>
	/// Validates multiple work entries
	/// </summary>
	ValidationResult ValidateMultiple(IEnumerable<WorkEntry> entries);

	/// <summary>
	/// Validates a worklog DTO
	/// </summary>
	ValidationResult Validate(WorklogDto worklog);
}
