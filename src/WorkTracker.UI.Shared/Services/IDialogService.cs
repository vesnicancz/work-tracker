using WorkTracker.Domain.Entities;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for showing dialogs in the application
/// Abstraction to keep ViewModels testable and independent of UI framework views
/// </summary>
public interface IDialogService
{
	/// <summary>
	/// Shows the work entry edit dialog for an existing entry
	/// </summary>
	/// <param name="workEntry">Work entry to edit</param>
	/// <returns>True if user confirmed, false if cancelled</returns>
	Task<bool> ShowEditWorkEntryDialogAsync(WorkEntry workEntry);

	/// <summary>
	/// Shows the work entry dialog for creating a new entry, optionally pre-filled with template values
	/// </summary>
	/// <param name="ticketId">Ticket ID to pre-fill</param>
	/// <param name="description">Description to pre-fill</param>
	/// <returns>True if user confirmed, false if cancelled</returns>
	Task<bool> ShowNewWorkEntryDialogAsync(string? ticketId = null, string? description = null);

	/// <summary>
	/// Shows the submit worklog dialog
	/// </summary>
	/// <param name="date">Date to submit worklogs for</param>
	/// <param name="isWeek">True to submit weekly, false for daily</param>
	/// <returns>True if user confirmed, false if cancelled</returns>
	Task<bool> ShowSubmitWorklogDialogAsync(DateTime? date = null, bool isWeek = false);

	/// <summary>
	/// Shows a confirmation dialog
	/// </summary>
	/// <param name="message">Message to display</param>
	/// <param name="title">Dialog title</param>
	/// <returns>True if user confirmed, false otherwise</returns>
	Task<bool> ShowConfirmationAsync(string message, string title = "Confirm");

	/// <summary>
	/// Shows an error message dialog
	/// </summary>
	/// <param name="message">Error message to display</param>
	/// <param name="title">Dialog title</param>
	Task ShowErrorAsync(string message, string title = "Error");

	/// <summary>
	/// Shows an information message dialog
	/// </summary>
	/// <param name="message">Message to display</param>
	/// <param name="title">Dialog title</param>
	Task ShowInformationAsync(string message, string title = "Information");

	/// <summary>
	/// Shows the settings dialog
	/// </summary>
	/// <returns>True if settings were saved, false if cancelled</returns>
	Task<bool> ShowSettingsDialogAsync();
}
