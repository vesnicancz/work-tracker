using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Represents a favorite work item that can be quickly started from the tray menu
/// </summary>
public class FavoriteWorkItem : INotifyPropertyChanged
{
	private string _name = string.Empty;
	private string? _ticketId;
	private string? _description;
	private bool _showAsTemplate;

	/// <summary>
	/// Unique identifier for this favorite
	/// </summary>
	public Guid Id { get; set; } = Guid.NewGuid();

	/// <summary>
	/// Display name shown in the tray menu
	/// </summary>
	public string Name
	{
		get => _name;
		set
		{
			if (_name != value)
			{
				_name = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>
	/// Ticket ID to use when starting work
	/// </summary>
	public string? TicketId
	{
		get => _ticketId;
		set
		{
			if (_ticketId != value)
			{
				_ticketId = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>
	/// Description to use when starting work
	/// </summary>
	public string? Description
	{
		get => _description;
		set
		{
			if (_description != value)
			{
				_description = value;
				OnPropertyChanged();
			}
		}
	}

	/// <summary>
	/// When true, clicking the favorite opens the entry dialog with pre-filled values
	/// instead of starting tracking immediately
	/// </summary>
	public bool ShowAsTemplate
	{
		get => _showAsTemplate;
		set
		{
			if (_showAsTemplate != value)
			{
				_showAsTemplate = value;
				OnPropertyChanged();
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
