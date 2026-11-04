using WorkTracker.Application.DTOs;

namespace WorkTracker.Application.Services;

/// <summary>
/// Service for validating worklogs before submission
/// </summary>
public interface IWorklogValidator
{
	/// <summary>
	/// Validates a worklog DTO
	/// </summary>
	ValidationResult Validate(WorklogDto worklog);
}
