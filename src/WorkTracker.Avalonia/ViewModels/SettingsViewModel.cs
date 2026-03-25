using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Settings window
/// </summary>
public class SettingsViewModel : ViewModelBase
{
	private readonly ISettingsService _settingsService;
	private readonly IPluginManager _pluginManager;
	private readonly IConfiguration _configuration;
	private readonly ILogger<SettingsViewModel> _logger;
	private readonly IAutostartManager _autostartManager;
	private readonly ITrayIconService _trayIconService;
	private readonly ILocalizationService _localization;
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
		ISettingsService settingsService,
		IPluginManager pluginManager,
		IConfiguration configuration,
		ILogger<SettingsViewModel> logger,
		IAutostartManager autostartManager,
		ITrayIconService trayIconService,
		ILocalizationService localization)
	{
		_settingsService = settingsService;
		_pluginManager = pluginManager;
		_configuration = configuration;
		_logger = logger;
		_autostartManager = autostartManager;
		_trayIconService = trayIconService;
		_localization = localization;

		// Load current settings
		_closeWindowBehavior = _settingsService.Settings.CloseWindowBehavior;
		// Check actual autostart status from registry (more reliable than saved setting)
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

		// Load plugins (do this last in case it throws)
		try
		{
			LoadPlugins();
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

				// Cancel add mode when user clicks an existing item
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

	#endregion

	#region Commands

	public IAsyncRelayCommand SaveCommand { get; }
	public ICommand CancelCommand { get; }
	public IAsyncRelayCommand TestConnectionCommand { get; }

	// Favorites commands
	public IRelayCommand AddFavoriteCommand { get; }
	public IRelayCommand SaveFavoriteCommand { get; }
	public ICommand CancelEditFavoriteCommand { get; }
	public IRelayCommand RemoveFavoriteCommand { get; }
	public IRelayCommand MoveFavoriteUpCommand { get; }
	public IRelayCommand MoveFavoriteDownCommand { get; }

	#endregion

	#region Command Implementations

	private async Task SaveAsync()
	{
		try
		{
			var settings = new ApplicationSettings
			{
				CloseWindowBehavior = CloseWindowBehavior,
				StartWithWindows = StartWithWindows,
				StartMinimized = StartMinimized,
				PluginConfigurations = new Dictionary<string, Dictionary<string, string>>(),
				EnabledPlugins = new Dictionary<string, bool>(),
				FavoriteWorkItems = FavoriteWorkItems.ToList(),
				Theme = SelectedTheme
			};

			// Save plugin configurations and enabled state
			foreach (var pluginVm in Plugins)
			{
				settings.PluginConfigurations[pluginVm.Plugin.Metadata.Id] =
					new Dictionary<string, string>(pluginVm.Configuration);
				settings.EnabledPlugins[pluginVm.Plugin.Metadata.Id] = pluginVm.IsEnabled;
			}

			await _settingsService.SaveSettingsAsync(settings);

			// Update enabled plugins in PluginManager
			var enabledPluginIds = Plugins.Where(p => p.IsEnabled).Select(p => p.Plugin.Metadata.Id);
			_pluginManager.SetEnabledPlugins(enabledPluginIds);

			// Re-initialize plugins with new configuration
			await _pluginManager.InitializePluginsAsync(settings.PluginConfigurations);

			// Apply autostart setting
			_autostartManager.SetAutostart(StartWithWindows);

			// Refresh tray menu favorites
			_trayIconService.RefreshFavoritesMenu();

			_logger.LogInformation("Settings saved successfully");

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

	private void LoadPlugins()
	{
		Plugins.Clear();

		foreach (var plugin in _pluginManager.LoadedPlugins.Values)
		{
			var pluginViewModel = new PluginViewModel(plugin);

			// Load enabled state (default to true if not found)
			if (_settingsService.Settings.EnabledPlugins.TryGetValue(plugin.Metadata.Id, out var isEnabled))
			{
				pluginViewModel.IsEnabled = isEnabled;
			}

			// First try to load from user settings
			if (_settingsService.Settings.PluginConfigurations.TryGetValue(plugin.Metadata.Id, out var savedConfig))
			{
				foreach (var kvp in savedConfig)
				{
					pluginViewModel.Configuration[kvp.Key] = kvp.Value;
				}
			}
			else
			{
				// Fall back to appsettings.json for initial configuration
				var configSection = _configuration.GetSection($"Plugins:{plugin.Metadata.Id}");
				foreach (var field in pluginViewModel.ConfigurationFields)
				{
					var value = configSection[field.Key];
					if (!string.IsNullOrEmpty(value))
					{
						pluginViewModel.Configuration[field.Key] = value;
					}
				}
			}

			// Notify ConfigurationFieldViewModels about loaded values
			foreach (var fieldVm in pluginViewModel.ConfigurationFields)
			{
				fieldVm.RefreshValue();
			}

			Plugins.Add(pluginViewModel);
		}

		if (Plugins.Any())
		{
			SelectedPlugin = Plugins.First();
		}
	}

	private async Task TestConnectionAsync()
	{
		if (SelectedPlugin == null)
		{
			return;
		}

		// Test connection is only available for worklog upload plugins
		if (SelectedPlugin.Plugin is not IWorklogUploadPlugin worklogPlugin)
		{
			TestConnectionResult = "✗ Test connection not available for this plugin type";
			return;
		}

		IsTestingConnection = true;
		TestConnectionResult = null;

		try
		{
			_logger.LogInformation("Testing connection for plugin {PluginId}", SelectedPlugin.Plugin.Metadata.Id);

			// Temporarily initialize plugin with current configuration
			var tempConfig = new Dictionary<string, string>(SelectedPlugin.Configuration);
			await SelectedPlugin.Plugin.InitializeAsync(tempConfig);

			var result = await worklogPlugin.TestConnectionAsync();

			if (result.IsSuccess)
			{
				TestConnectionResult = "✓ Connection successful";
				_logger.LogInformation("Connection test successful for {PluginId}", SelectedPlugin.Plugin.Metadata.Id);
			}
			else
			{
				TestConnectionResult = $"✗ Connection failed: {result.Error}";
				_logger.LogWarning("Connection test failed for {PluginId}: {Error}",
					SelectedPlugin.Plugin.Metadata.Id, result.Error);
			}
		}
		catch (Exception ex)
		{
			TestConnectionResult = $"✗ Error: {ex.Message}";
			_logger.LogError(ex, "Error testing connection for {PluginId}", SelectedPlugin?.Plugin.Metadata.Id);
		}
		finally
		{
			IsTestingConnection = false;
		}
	}

	#endregion

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
			// Add new favorite
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
		if (SelectedFavorite != null)
		{
			var index = FavoriteWorkItems.IndexOf(SelectedFavorite);
			FavoriteWorkItems.Remove(SelectedFavorite);

			// Select next item or previous if at end
			if (FavoriteWorkItems.Count > 0)
			{
				SelectedFavorite = FavoriteWorkItems[Math.Min(index, FavoriteWorkItems.Count - 1)];
			}
			else
			{
				SelectedFavorite = null;
				ClearEditingFields();
			}
		}
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

	private bool CanMoveFavoriteUp()
	{
		return SelectedFavorite != null && FavoriteWorkItems.IndexOf(SelectedFavorite) > 0;
	}

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

	private bool CanMoveFavoriteDown()
	{
		return SelectedFavorite != null && FavoriteWorkItems.IndexOf(SelectedFavorite) < FavoriteWorkItems.Count - 1;
	}

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

	#endregion
}

/// <summary>
/// ViewModel for a plugin configuration
/// </summary>
public class PluginViewModel : ViewModelBase
{
	private bool _isEnabled = true;

	public IPlugin Plugin { get; }
	public Dictionary<string, string> Configuration { get; } = new();
	public ObservableCollection<ConfigurationFieldViewModel> ConfigurationFields { get; } = new();

	public PluginViewModel(IPlugin plugin)
	{
		Plugin = plugin;

		// Initialize configuration with default values and create field view models
		// Only worklog upload plugins have configuration fields
		if (plugin is IWorklogUploadPlugin worklogPlugin)
		{
			foreach (var field in worklogPlugin.GetConfigurationFields())
			{
				var value = string.Empty;
				if (!string.IsNullOrEmpty(field.DefaultValue))
				{
					value = field.DefaultValue;
					Configuration[field.Key] = value;
				}

				ConfigurationFields.Add(new ConfigurationFieldViewModel(field, this));
			}
		}
	}

	public string Name => Plugin.Metadata.Name;
	public string Description => Plugin.Metadata.Description ?? string.Empty;
	public string Version => Plugin.Metadata.Version.ToString();
	public string Author => Plugin.Metadata.Author;
	public bool SupportsTestConnection => Plugin is IWorklogUploadPlugin;

	public bool IsEnabled
	{
		get => _isEnabled;
		set => SetProperty(ref _isEnabled, value);
	}
}

/// <summary>
/// ViewModel for a configuration field
/// </summary>
public class ConfigurationFieldViewModel : ViewModelBase
{
	private readonly PluginConfigurationField _field;
	private readonly PluginViewModel _pluginViewModel;

	public ConfigurationFieldViewModel(PluginConfigurationField field, PluginViewModel pluginViewModel)
	{
		_field = field;
		_pluginViewModel = pluginViewModel;
	}

	public string Key => _field.Key;
	public string Label => _field.Label;
	public string? Description => _field.Description;
	public string? Placeholder => _field.Placeholder;
	public PluginConfigurationFieldType Type => _field.Type;
	public bool IsRequired => _field.IsRequired;

	public string Value
	{
		get => _pluginViewModel.Configuration.TryGetValue(Key, out var value) ? value : string.Empty;
		set
		{
			if (_pluginViewModel.Configuration.TryGetValue(Key, out var current) && current == value)
			{
				return;
			}

			_pluginViewModel.Configuration[Key] = value;
			OnPropertyChanged();
		}
	}

	public void RefreshValue()
	{
		OnPropertyChanged(nameof(Value));
	}
}
