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
	private readonly IThemeService _themeService;
	private readonly ILogger<SettingsViewModel> _logger;
	private CloseWindowBehavior _closeWindowBehavior;
	private bool _startWithWindows;
	private bool _startMinimized;
	private bool _checkForUpdates;
	private PluginViewModel? _selectedPlugin;
	private string? _testConnectionResult;
	private bool _isTestingConnection;

	// Pomodoro
	private bool _pomodoroEnabled;
	private int _pomodoroWorkMinutes;
	private int _pomodoroShortBreakMinutes;
	private int _pomodoroLongBreakMinutes;
	private int _pomodorosBeforeLongBreak;
	private bool _pomodoroAutoStartTracking;
	private bool _pomodoroAutoStopTracking;

	// Favorites
	private FavoriteWorkItem? _selectedFavorite;

	private string _editingFavoriteName = string.Empty;
	private string _editingFavoriteTicket = string.Empty;
	private string _editingFavoriteDescription = string.Empty;
	private bool _editingFavoriteShowAsTemplate;
	private bool _isAddingFavorite;
	private string _selectedTheme = ApplicationSettings.DefaultTheme;
	private bool _followSystemTheme;
	private string _selectedLightTheme = ThemeCatalog.DefaultLightTheme;
	private string _selectedDarkTheme = ThemeCatalog.DefaultDarkTheme;
	private readonly string _initialLanguage;
	private LanguageOptionViewModel _selectedLanguage;

	// Snapshot of everything previewed live, taken before the user can touch anything, so every
	// dismissal path can put the application back exactly where it found it.
	private readonly bool _initialFollowSystemTheme;
	private readonly string _initialTheme;
	private readonly string _initialLightTheme;
	private readonly string _initialDarkTheme;

	public SettingsViewModel(
		ISettingsOrchestrator orchestrator,
		ISettingsService settingsService,
		ILogger<SettingsViewModel> logger,
		IAutostartManager autostartManager,
		ILocalizationService localization,
		IThemeService themeService)
	{
		_orchestrator = orchestrator;
		_settingsService = settingsService;
		_logger = logger;
		_autostartManager = autostartManager;
		_localization = localization;
		_themeService = themeService;

		// Load current settings
		_closeWindowBehavior = _settingsService.Settings.CloseWindowBehavior;
		_startWithWindows = _autostartManager.IsEnabled;
		_startMinimized = _settingsService.Settings.StartMinimized;
		_checkForUpdates = _settingsService.Settings.CheckForUpdates;
		_selectedTheme = _settingsService.Settings.Theme ?? ApplicationSettings.DefaultTheme;
		_followSystemTheme = _settingsService.Settings.FollowSystemTheme;
		_selectedLightTheme = ResolveLightTheme(_settingsService.Settings.LightTheme);
		_selectedDarkTheme = ResolveDarkTheme(_settingsService.Settings.DarkTheme);

		_initialFollowSystemTheme = _followSystemTheme;
		_initialTheme = _selectedTheme;
		_initialLightTheme = _selectedLightTheme;
		_initialDarkTheme = _selectedDarkTheme;

		_initialLanguage = LanguageCatalog.Normalize(_settingsService.Settings.Language);
		AvailableLanguages =
		[
			new LanguageOptionViewModel(LanguageCatalog.SystemLanguage, localization, "LanguageSystem"),
			.. LanguageCatalog.SupportedLanguages.Select(code => new LanguageOptionViewModel(code, localization))
		];
		_selectedLanguage = AvailableLanguages.First(option => option.Code == _initialLanguage);

		// Load Pomodoro settings
		var pomodoro = _settingsService.Settings.Pomodoro;
		_pomodoroEnabled = pomodoro.Enabled;
		_pomodoroWorkMinutes = pomodoro.WorkMinutes;
		_pomodoroShortBreakMinutes = pomodoro.ShortBreakMinutes;
		_pomodoroLongBreakMinutes = pomodoro.LongBreakMinutes;
		_pomodorosBeforeLongBreak = pomodoro.PomodorosBeforeLongBreak;
		_pomodoroAutoStartTracking = pomodoro.AutoStartWorkTracking;
		_pomodoroAutoStopTracking = pomodoro.AutoStopWorkTracking;

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

			OnPropertyChanged(nameof(HasPlugins));
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

	public bool CheckForUpdates
	{
		get => _checkForUpdates;
		set => SetProperty(ref _checkForUpdates, value);
	}

	public string AppVersionDisplay => _localization.GetFormattedString("VersionFormat", Application.AppInfo.DisplayVersion);
	public string RuntimeVersion => $".NET {Environment.Version}";
	public string PlatformInfo => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

	public string[] AvailableThemes { get; } = ThemeCatalog.AllThemes;

	public string[] AvailableLightThemes { get; } = ThemeCatalog.LightThemesSorted;

	public string[] AvailableDarkThemes { get; } = ThemeCatalog.DarkThemesSorted;

	public string SelectedTheme
	{
		get => _selectedTheme;
		set
		{
			if (SetProperty(ref _selectedTheme, value))
			{
				ApplyThemePreview();
			}
		}
	}

	public bool FollowSystemTheme
	{
		get => _followSystemTheme;
		set
		{
			if (SetProperty(ref _followSystemTheme, value))
			{
				OnPropertyChanged(nameof(IsSingleThemeMode));
				ApplyThemePreview();
			}
		}
	}

	public bool IsSingleThemeMode => !_followSystemTheme;

	public string SelectedLightTheme
	{
		get => _selectedLightTheme;
		set
		{
			if (SetProperty(ref _selectedLightTheme, value))
			{
				ApplyThemePreview();
			}
		}
	}

	public string SelectedDarkTheme
	{
		get => _selectedDarkTheme;
		set
		{
			if (SetProperty(ref _selectedDarkTheme, value))
			{
				ApplyThemePreview();
			}
		}
	}

	public IReadOnlyList<LanguageOptionViewModel> AvailableLanguages { get; }

	public LanguageOptionViewModel SelectedLanguage
	{
		get => _selectedLanguage;
		set
		{
			if (value is not null && SetProperty(ref _selectedLanguage, value))
			{
				ApplyLanguagePreview(value.Code);
			}
		}
	}

	/// <summary>
	/// Switches the UI language immediately, mirroring the theme live preview. Persisted only on
	/// Save; <see cref="RevertPreview"/> undoes it when the dialog is dismissed.
	/// </summary>
	private void ApplyLanguagePreview(string languageCode)
	{
		_localization.ApplyLanguage(languageCode);

		// The service repaints XAML {markup:Localize} bindings; these refresh what this ViewModel
		// computed from resources itself - the translated "System" label and AppVersionDisplay.
		foreach (var option in AvailableLanguages)
		{
			option.RefreshDisplayName();
		}

		OnPropertyChanged(string.Empty);
	}

	/// <summary>
	/// Undoes every live preview the dialog applied - language and theme alike - restoring the
	/// state it opened with. Covers all dismissal paths: Cancel and the titlebar X, which closes
	/// the window without going through the command. Safe to call twice; each revert is a no-op
	/// when nothing changed.
	/// </summary>
	public void RevertPreview()
	{
		RevertLanguagePreview();
		RevertThemePreview();
	}

	private void RevertLanguagePreview()
	{
		if (SelectedLanguage.Code != _initialLanguage)
		{
			SelectedLanguage = AvailableLanguages.First(option => option.Code == _initialLanguage);
		}
	}

	/// <summary>
	/// Restores all four theme fields at once, then applies them in a single pass - going through
	/// the public setters instead would repaint the app up to four times on the way back.
	/// </summary>
	private void RevertThemePreview()
	{
		if (_followSystemTheme == _initialFollowSystemTheme
			&& _selectedTheme == _initialTheme
			&& _selectedLightTheme == _initialLightTheme
			&& _selectedDarkTheme == _initialDarkTheme)
		{
			return;
		}

		SetProperty(ref _followSystemTheme, _initialFollowSystemTheme, nameof(FollowSystemTheme));
		SetProperty(ref _selectedTheme, _initialTheme, nameof(SelectedTheme));
		SetProperty(ref _selectedLightTheme, _initialLightTheme, nameof(SelectedLightTheme));
		SetProperty(ref _selectedDarkTheme, _initialDarkTheme, nameof(SelectedDarkTheme));
		OnPropertyChanged(nameof(IsSingleThemeMode));

		ApplyThemePreview();
	}

	private void ApplyThemePreview()
	{
		_themeService.ApplyThemeMode(_followSystemTheme, _selectedTheme, _selectedLightTheme, _selectedDarkTheme);
	}

	private static string ResolveLightTheme(string? saved)
		=> !string.IsNullOrEmpty(saved) && ThemeCatalog.IsLight(saved) ? saved : ThemeCatalog.DefaultLightTheme;

	private static string ResolveDarkTheme(string? saved)
		=> !string.IsNullOrEmpty(saved) && !ThemeCatalog.IsLight(saved) ? saved : ThemeCatalog.DefaultDarkTheme;

	public Action? CloseAction { get; set; }
	public bool DialogResult { get; set; }

	public ObservableCollection<PluginViewModel> Plugins { get; } = new();

	public bool HasPlugins => Plugins.Count > 0;

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

	// Pomodoro properties
	public bool PomodoroEnabled
	{
		get => _pomodoroEnabled;
		set => SetProperty(ref _pomodoroEnabled, value);
	}

	public int PomodoroWorkMinutes
	{
		get => _pomodoroWorkMinutes;
		set => SetProperty(ref _pomodoroWorkMinutes, value);
	}

	public int PomodoroShortBreakMinutes
	{
		get => _pomodoroShortBreakMinutes;
		set => SetProperty(ref _pomodoroShortBreakMinutes, value);
	}

	public int PomodoroLongBreakMinutes
	{
		get => _pomodoroLongBreakMinutes;
		set => SetProperty(ref _pomodoroLongBreakMinutes, value);
	}

	public int PomodorosBeforeLongBreak
	{
		get => _pomodorosBeforeLongBreak;
		set => SetProperty(ref _pomodorosBeforeLongBreak, value);
	}

	public bool PomodoroAutoStartTracking
	{
		get => _pomodoroAutoStartTracking;
		set => SetProperty(ref _pomodoroAutoStartTracking, value);
	}

	public bool PomodoroAutoStopTracking
	{
		get => _pomodoroAutoStopTracking;
		set => SetProperty(ref _pomodoroAutoStopTracking, value);
	}

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
				CheckForUpdates = CheckForUpdates,
				Theme = SelectedTheme,
				FollowSystemTheme = FollowSystemTheme,
				LightTheme = SelectedLightTheme,
				DarkTheme = SelectedDarkTheme,
				Language = SelectedLanguage.Code,
				FavoriteWorkItems = FavoriteWorkItems.ToList(),
				Plugins = Plugins.ToList(),
				Pomodoro = new PomodoroSettings
				{
					Enabled = PomodoroEnabled,
					WorkMinutes = PomodoroWorkMinutes,
					ShortBreakMinutes = PomodoroShortBreakMinutes,
					LongBreakMinutes = PomodoroLongBreakMinutes,
					PomodorosBeforeLongBreak = PomodorosBeforeLongBreak,
					AutoStartWorkTracking = PomodoroAutoStartTracking,
					AutoStopWorkTracking = PomodoroAutoStopTracking
				}
			};

			await _orchestrator.SaveSettingsAsync(request, CancellationToken.None);

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
		RevertPreview();
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
			var progress = new Progress<string>(message => TestConnectionResult = message);
			TestConnectionResult = await _orchestrator.TestConnectionAsync(SelectedPlugin, progress, CancellationToken.None);
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

	/// <summary>
	/// Copies each favorite instead of binding the live settings objects. Editing a favorite
	/// mutates the item in place, so sharing instances with <see cref="ISettingsService.Settings"/>
	/// would leak unsaved edits into the tray menu and into the next save made from anywhere else -
	/// Cancel could not undo them. The copy keeps <see cref="FavoriteWorkItem.Id"/> so saving
	/// still updates the same favorite rather than replacing it.
	/// </summary>
	private void LoadFavorites()
	{
		FavoriteWorkItems.Clear();
		foreach (var favorite in _settingsService.Settings.FavoriteWorkItems)
		{
			FavoriteWorkItems.Add(CopyOf(favorite));
		}
	}

	private static FavoriteWorkItem CopyOf(FavoriteWorkItem source) => new()
	{
		Id = source.Id,
		Name = source.Name,
		TicketId = source.TicketId,
		Description = source.Description,
		ShowAsTemplate = source.ShowAsTemplate
	};

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