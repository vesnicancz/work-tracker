namespace WorkTracker.WPF.Models;

/// <summary>
/// Represents a favorite work item that can be quickly started from the tray menu
/// </summary>
public class FavoriteWorkItem
{
	/// <summary>
	/// Unique identifier for this favorite
	/// </summary>
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>
	/// Display name shown in the tray menu
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Ticket ID to use when starting work
	/// </summary>
	public string? TicketId { get; set; }

	/// <summary>
	/// Description to use when starting work
	/// </summary>
	public string? Description { get; set; }
}
