using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WorkTracker.Avalonia.Models;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// The Favorites tab of the Settings window: the list and the form editing one of its items
/// </summary>
public class FavoritesSettingsViewModel : ViewModelBase
{
	private FavoriteWorkItem? _selectedItem;
	private string _editingName = string.Empty;
	private string _editingTicket = string.Empty;
	private string _editingDescription = string.Empty;
	private bool _editingShowAsTemplate;
	private bool _isAdding;

	/// <summary>
	/// Copies each favorite instead of binding the live settings objects. Editing a favorite
	/// mutates the item in place, so sharing instances with <see cref="Services.ISettingsService.Settings"/>
	/// would leak unsaved edits into the tray menu and into the next save made from anywhere else -
	/// Cancel could not undo them. The copy keeps <see cref="FavoriteWorkItem.Id"/> so saving
	/// still updates the same favorite rather than replacing it.
	/// </summary>
	public FavoritesSettingsViewModel(IEnumerable<FavoriteWorkItem> favorites)
	{
		foreach (var favorite in favorites)
		{
			Items.Add(CopyOf(favorite));
		}

		AddCommand = new RelayCommand(Add);
		SaveCommand = new RelayCommand(Save, () => !string.IsNullOrWhiteSpace(EditingName));
		CancelEditCommand = new RelayCommand(CancelEdit);
		RemoveCommand = new RelayCommand(Remove, () => SelectedItem != null);
		MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
		MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
	}

	public ObservableCollection<FavoriteWorkItem> Items { get; } = new();

	public FavoriteWorkItem? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (SetProperty(ref _selectedItem, value))
			{
				RemoveCommand.NotifyCanExecuteChanged();
				MoveUpCommand.NotifyCanExecuteChanged();
				MoveDownCommand.NotifyCanExecuteChanged();
				OnPropertyChanged(nameof(IsEditFormVisible));

				if (value != null && IsAdding)
				{
					IsAdding = false;
				}

				if (value != null)
				{
					LoadEditingFields(value);
				}
			}
		}
	}

	public string EditingName
	{
		get => _editingName;
		set
		{
			if (SetProperty(ref _editingName, value))
			{
				SaveCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string EditingTicket
	{
		get => _editingTicket;
		set => SetProperty(ref _editingTicket, value);
	}

	public string EditingDescription
	{
		get => _editingDescription;
		set => SetProperty(ref _editingDescription, value);
	}

	public bool EditingShowAsTemplate
	{
		get => _editingShowAsTemplate;
		set => SetProperty(ref _editingShowAsTemplate, value);
	}

	public bool IsAdding
	{
		get => _isAdding;
		set
		{
			if (SetProperty(ref _isAdding, value))
			{
				OnPropertyChanged(nameof(IsEditFormVisible));
			}
		}
	}

	public bool IsEditFormVisible => SelectedItem != null || IsAdding;

	public IRelayCommand AddCommand { get; }
	public IRelayCommand SaveCommand { get; }
	public ICommand CancelEditCommand { get; }
	public IRelayCommand RemoveCommand { get; }
	public IRelayCommand MoveUpCommand { get; }
	public IRelayCommand MoveDownCommand { get; }

	private static FavoriteWorkItem CopyOf(FavoriteWorkItem source) => new()
	{
		Id = source.Id,
		Name = source.Name,
		TicketId = source.TicketId,
		Description = source.Description,
		ShowAsTemplate = source.ShowAsTemplate
	};

	private void Add()
	{
		IsAdding = true;
		SelectedItem = null;
		ClearEditingFields();
	}

	private void Save()
	{
		if (string.IsNullOrWhiteSpace(EditingName))
		{
			return;
		}

		if (SelectedItem != null && !IsAdding)
		{
			SelectedItem.Name = EditingName;
			SelectedItem.TicketId = string.IsNullOrWhiteSpace(EditingTicket) ? null : EditingTicket;
			SelectedItem.Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription;
			SelectedItem.ShowAsTemplate = EditingShowAsTemplate;
		}
		else
		{
			var newFavorite = new FavoriteWorkItem
			{
				Name = EditingName,
				TicketId = string.IsNullOrWhiteSpace(EditingTicket) ? null : EditingTicket,
				Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription,
				ShowAsTemplate = EditingShowAsTemplate
			};
			Items.Add(newFavorite);
			SelectedItem = newFavorite;
		}

		IsAdding = false;
	}

	private void CancelEdit()
	{
		IsAdding = false;
		if (SelectedItem != null)
		{
			LoadEditingFields(SelectedItem);
		}
		else
		{
			ClearEditingFields();
		}
	}

	private void Remove()
	{
		if (SelectedItem == null)
		{
			return;
		}

		var index = Items.IndexOf(SelectedItem);
		Items.Remove(SelectedItem);
		if (Items.Count > 0)
		{
			SelectedItem = Items[Math.Min(index, Items.Count - 1)];
		}
		else
		{
			SelectedItem = null;
			ClearEditingFields();
		}
	}

	private void LoadEditingFields(FavoriteWorkItem item)
	{
		EditingName = item.Name;
		EditingTicket = item.TicketId ?? string.Empty;
		EditingDescription = item.Description ?? string.Empty;
		EditingShowAsTemplate = item.ShowAsTemplate;
	}

	private void ClearEditingFields()
	{
		EditingName = string.Empty;
		EditingTicket = string.Empty;
		EditingDescription = string.Empty;
		EditingShowAsTemplate = false;
	}

	private bool CanMoveUp() => SelectedItem != null && Items.IndexOf(SelectedItem) > 0;

	private void MoveUp()
	{
		if (SelectedItem == null)
		{
			return;
		}

		var item = SelectedItem;
		var index = Items.IndexOf(item);
		if (index > 0)
		{
			Items.Move(index, index - 1);
			SelectedItem = item;
			MoveUpCommand.NotifyCanExecuteChanged();
			MoveDownCommand.NotifyCanExecuteChanged();
		}
	}

	private bool CanMoveDown() => SelectedItem != null && Items.IndexOf(SelectedItem) < Items.Count - 1;

	private void MoveDown()
	{
		if (SelectedItem == null)
		{
			return;
		}

		var item = SelectedItem;
		var index = Items.IndexOf(item);
		if (index < Items.Count - 1)
		{
			Items.Move(index, index + 1);
			SelectedItem = item;
			MoveUpCommand.NotifyCanExecuteChanged();
			MoveDownCommand.NotifyCanExecuteChanged();
		}
	}
}
