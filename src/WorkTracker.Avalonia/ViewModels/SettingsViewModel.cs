using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Settings window
/// </summary>
public class SettingsViewModel : ViewModelBase
{
	private readonly ISettingsOrchestrator _orchestrator;
	private readonly ISettingsService _settingsService;
	private readonly IAutostartManager _autostartManager;
	private readonly ILocalizationService _localization;
	private readonly ILogger<SettingsViewModel> _logger;
	private CloseWindowBehavior _closeWindowBehavior;
	private bool _startWithWindows;
	private bool _startMinimized;
	private PluginViewModel? _selectedPlugin;
	private string? _testConnectionResult;
	private bool _isTestingConnection;

	// Favorites
	private FavoriteWorkItem? _selectedFavorite;

	private string _editingFavoriteName = string.Empty;
	private string _editingFavoriteTicket = string.Empty;
	private string _editingFavoriteDescription = string.Empty;
	private bool _editingFavoriteShowAsTemplate;
	private bool _isAddingFavorite;
	private string _selectedTheme = "Dark";

	public SettingsViewModel(
		ISettingsOrchestrator orchestrator,
		ISettingsService settingsService,
		ILogger<SettingsViewModel> logger,
		IAutostartManager autostartManager,
		ILocalizationService localization)
	{
		_orchestrator = orchestrator;
		_settingsService = settingsService;
		_logger = logger;
		_autostartManager = autostartManager;
		_localization = localization;

		// Load current settings
		_closeWindowBehavior = _settingsService.Settings.CloseWindowBehavior;
		_startWithWindows = _autostartManager.IsEnabled;
		_startMinimized = _settingsService.Settings.StartMinimized;
		_selectedTheme = _settingsService.Settings.Theme ?? "Dark";

		// Initialize commands
		SaveCommand = new AsyncRelayCommand(SaveAsync);
		CancelCommand = new RelayCommand(Cancel);
		TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => SelectedPlugin != null && !IsTestingConnection);

		// Initialize favorite commands
		AddFavoriteCommand = new RelayCommand(AddFavorite);
		SaveFavoriteCommand = new RelayCommand(SaveFavorite, () => !string.IsNullOrWhiteSpace(EditingFavoriteName));
		CancelEditFavoriteCommand = new RelayCommand(CancelEditFavorite);
		RemoveFavoriteCommand = new RelayCommand(RemoveFavorite, () => SelectedFavorite != null);
		MoveFavoriteUpCommand = new RelayCommand(MoveFavoriteUp, CanMoveFavoriteUp);
		MoveFavoriteDownCommand = new RelayCommand(MoveFavoriteDown, CanMoveFavoriteDown);

		// Load favorites
		LoadFavorites();

		// Load plugins
		try
		{
			var plugins = _orchestrator.LoadPlugins();
			foreach (var p in plugins)
			{
				Plugins.Add(p);
			}

			if (Plugins.Any())
			{
				SelectedPlugin = Plugins.First();
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load plugins in SettingsViewModel");
		}
	}

	#region Properties

	public CloseWindowBehavior CloseWindowBehavior
	{
		get => _closeWindowBehavior;
		set => SetProperty(ref _closeWindowBehavior, value);
	}

	public bool IsMinimizeToTray
	{
		get => CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray;
		set
		{
			if (value)
			{
				CloseWindowBehavior = CloseWindowBehavior.MinimizeToTray;
			}
		}
	}

	public bool IsExitApplication
	{
		get => CloseWindowBehavior == CloseWindowBehavior.ExitApplication;
		set
		{
			if (value)
			{
				CloseWindowBehavior = CloseWindowBehavior.ExitApplication;
			}
		}
	}

	public bool StartWithWindows
	{
		get => _startWithWindows;
		set => SetProperty(ref _startWithWindows, value);
	}

	public bool StartMinimized
	{
		get => _startMinimized;
		set => SetProperty(ref _startMinimized, value);
	}

	public string AppVersionDisplay => _localization.GetFormattedString("VersionFormat", Application.AppInfo.Version);
	public string RuntimeVersion => $".NET {Environment.Version}";
	public string PlatformInfo => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

	public string[] AvailableThemes { get; } = [.. new[] { "Dark", "Light", "Midnight", "Modern Blue", "Purple" }.OrderBy(t => t)];

	public string SelectedTheme
	{
		get => _selectedTheme;
		set
		{
			if (SetProperty(ref _selectedTheme, value))
			{
				App.SwitchTheme(value);
			}
		}
	}

	public Action? CloseAction { get; set; }
	public bool DialogResult { get; set; }

	public ObservableCollection<PluginViewModel> Plugins { get; } = new();

	public PluginViewModel? SelectedPlugin
	{
		get => _selectedPlugin;
		set
		{
			if (SetProperty(ref _selectedPlugin, value))
			{
				TestConnectionResult = null;
				TestConnectionCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string? TestConnectionResult
	{
		get => _testConnectionResult;
		set => SetProperty(ref _testConnectionResult, value);
	}

	public bool IsTestingConnection
	{
		get => _isTestingConnection;
		set
		{
			if (SetProperty(ref _isTestingConnection, value))
			{
				TestConnectionCommand.NotifyCanExecuteChanged();
			}
		}
	}

	// Favorites properties
	public ObservableCollection<FavoriteWorkItem> FavoriteWorkItems { get; } = new();

	public FavoriteWorkItem? SelectedFavorite
	{
		get => _selectedFavorite;
		set
		{
			if (SetProperty(ref _selectedFavorite, value))
			{
				RemoveFavoriteCommand.NotifyCanExecuteChanged();
				MoveFavoriteUpCommand.NotifyCanExecuteChanged();
				MoveFavoriteDownCommand.NotifyCanExecuteChanged();
				OnPropertyChanged(nameof(IsEditFormVisible));

				if (value != null && IsAddingFavorite)
				{
					IsAddingFavorite = false;
				}

				if (value != null)
				{
					LoadEditingFields(value);
				}
			}
		}
	}

	public string EditingFavoriteName
	{
		get => _editingFavoriteName;
		set
		{
			if (SetProperty(ref _editingFavoriteName, value))
			{
				SaveFavoriteCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string EditingFavoriteTicket
	{
		get => _editingFavoriteTicket;
		set => SetProperty(ref _editingFavoriteTicket, value);
	}

	public string EditingFavoriteDescription
	{
		get => _editingFavoriteDescription;
		set => SetProperty(ref _editingFavoriteDescription, value);
	}

	public bool EditingFavoriteShowAsTemplate
	{
		get => _editingFavoriteShowAsTemplate;
		set => SetProperty(ref _editingFavoriteShowAsTemplate, value);
	}

	public bool IsAddingFavorite
	{
		get => _isAddingFavorite;
		set
		{
			if (SetProperty(ref _isAddingFavorite, value))
			{
				OnPropertyChanged(nameof(IsEditFormVisible));
			}
		}
	}

	public bool IsEditFormVisible => SelectedFavorite != null || IsAddingFavorite;

	#endregion Properties

	#region Commands

	public IAsyncRelayCommand SaveCommand { get; }
	public ICommand CancelCommand { get; }
	public IAsyncRelayCommand TestConnectionCommand { get; }

	public IRelayCommand AddFavoriteCommand { get; }
	public IRelayCommand SaveFavoriteCommand { get; }
	public ICommand CancelEditFavoriteCommand { get; }
	public IRelayCommand RemoveFavoriteCommand { get; }
	public IRelayCommand MoveFavoriteUpCommand { get; }
	public IRelayCommand MoveFavoriteDownCommand { get; }

	#endregion Commands

	#region Command Implementations

	private async Task SaveAsync()
	{
		try
		{
			var request = new SettingsSaveRequest
			{
				CloseWindowBehavior = CloseWindowBehavior,
				StartWithWindows = StartWithWindows,
				StartMinimized = StartMinimized,
				Theme = SelectedTheme,
				FavoriteWorkItems = FavoriteWorkItems.ToList(),
				Plugins = Plugins.ToList()
			};

			await _orchestrator.SaveSettingsAsync(request);

			DialogResult = true;
			CloseAction?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to save settings");
			DialogResult = false;
		}
	}

	private void Cancel()
	{
		DialogResult = false;
		CloseAction?.Invoke();
	}

	private async Task TestConnectionAsync()
	{
		if (SelectedPlugin == null)
		{
			return;
		}

		var pluginId = SelectedPlugin.Plugin.Metadata.Id;
		IsTestingConnection = true;
		TestConnectionResult = null;

		try
		{
			TestConnectionResult = await _orchestrator.TestConnectionAsync(SelectedPlugin);
		}
		catch (Exception ex)
		{
			TestConnectionResult = $"✗ Error: {ex.Message}";
			_logger.LogError(ex, "Error testing connection for {PluginId}", pluginId);
		}
		finally
		{
			IsTestingConnection = false;
		}
	}

	#endregion Command Implementations

	#region Favorites

	private void LoadFavorites()
	{
		FavoriteWorkItems.Clear();
		foreach (var favorite in _settingsService.Settings.FavoriteWorkItems)
		{
			FavoriteWorkItems.Add(favorite);
		}
	}

	private void AddFavorite()
	{
		IsAddingFavorite = true;
		SelectedFavorite = null;
		ClearEditingFields();
	}

	private void SaveFavorite()
	{
		if (string.IsNullOrWhiteSpace(EditingFavoriteName))
		{
			return;
		}

		if (SelectedFavorite != null && !IsAddingFavorite)
		{
			SelectedFavorite.Name = EditingFavoriteName;
			SelectedFavorite.TicketId = string.IsNullOrWhiteSpace(EditingFavoriteTicket) ? null : EditingFavoriteTicket;
			SelectedFavorite.Description = string.IsNullOrWhiteSpace(EditingFavoriteDescription) ? null : EditingFavoriteDescription;
			SelectedFavorite.ShowAsTemplate = EditingFavoriteShowAsTemplate;
		}
		else
		{
			var newFavorite = new FavoriteWorkItem
			{
				Name = EditingFavoriteName,
				TicketId = string.IsNullOrWhiteSpace(EditingFavoriteTicket) ? null : EditingFavoriteTicket,
				Description = string.IsNullOrWhiteSpace(EditingFavoriteDescription) ? null : EditingFavoriteDescription,
				ShowAsTemplate = EditingFavoriteShowAsTemplate
			};
			FavoriteWorkItems.Add(newFavorite);
			SelectedFavorite = newFavorite;
		}

		IsAddingFavorite = false;
	}

	private void CancelEditFavorite()
	{
		IsAddingFavorite = false;
		if (SelectedFavorite != null)
		{
			LoadEditingFields(SelectedFavorite);
		}
		else
		{
			ClearEditingFields();
		}
	}

	private void RemoveFavorite()
	{
		if (SelectedFavorite == null)
		{
			return;
		}

		var index = FavoriteWorkItems.IndexOf(SelectedFavorite);
		FavoriteWorkItems.Remove(SelectedFavorite);
		if (FavoriteWorkItems.Count > 0)
		{
			SelectedFavorite = FavoriteWorkItems[Math.Min(index, FavoriteWorkItems.Count - 1)];
		}
		else { SelectedFavorite = null; ClearEditingFields(); }
	}

	private void LoadEditingFields(FavoriteWorkItem item)
	{
		EditingFavoriteName = item.Name;
		EditingFavoriteTicket = item.TicketId ?? string.Empty;
		EditingFavoriteDescription = item.Description ?? string.Empty;
		EditingFavoriteShowAsTemplate = item.ShowAsTemplate;
	}

	private void ClearEditingFields()
	{
		EditingFavoriteName = string.Empty;
		EditingFavoriteTicket = string.Empty;
		EditingFavoriteDescription = string.Empty;
		EditingFavoriteShowAsTemplate = false;
	}

	private bool CanMoveFavoriteUp() => SelectedFavorite != null && FavoriteWorkItems.IndexOf(SelectedFavorite) > 0;

	private void MoveFavoriteUp()
	{
		if (SelectedFavorite == null)
		{
			return;
		}

		var item = SelectedFavorite;
		var index = FavoriteWorkItems.IndexOf(item);
		if (index > 0)
		{
			FavoriteWorkItems.Move(index, index - 1);
			SelectedFavorite = item;
			MoveFavoriteUpCommand.NotifyCanExecuteChanged();
			MoveFavoriteDownCommand.NotifyCanExecuteChanged();
		}
	}

	private bool CanMoveFavoriteDown() => SelectedFavorite != null && FavoriteWorkItems.IndexOf(SelectedFavorite) < FavoriteWorkItems.Count - 1;

	private void MoveFavoriteDown()
	{
		if (SelectedFavorite == null)
		{
			return;
		}

		var item = SelectedFavorite;
		var index = FavoriteWorkItems.IndexOf(item);
		if (index < FavoriteWorkItems.Count - 1)
		{
			FavoriteWorkItems.Move(index, index + 1);
			SelectedFavorite = item;
			MoveFavoriteUpCommand.NotifyCanExecuteChanged();
			MoveFavoriteDownCommand.NotifyCanExecuteChanged();
		}
	}

	#endregion Favorites
}