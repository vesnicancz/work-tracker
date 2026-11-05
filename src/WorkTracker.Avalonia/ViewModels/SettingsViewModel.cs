using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Settings window: the general settings, and saving or dismissing the whole dialog.
/// The other tabs have a view model each.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
	private readonly ISettingsOrchestrator _orchestrator;
	private readonly ILocalizationService _localization;
	private readonly ILogger<SettingsViewModel> _logger;
	private CloseWindowBehavior _closeWindowBehavior;
	private bool _startWithWindows;
	private bool _startMinimized;
	private bool _checkForUpdates;
	private bool _showRemoteControlNotifications;
	private string? _saveError;

	public SettingsViewModel(
		ISettingsOrchestrator orchestrator,
		ISettingsService settingsService,
		ILogger<SettingsViewModel> logger,
		IAutostartManager autostartManager,
		ILocalizationService localization,
		IThemeService themeService)
	{
		_orchestrator = orchestrator;
		_logger = logger;
		_localization = localization;

		var settings = settingsService.Settings;
		_closeWindowBehavior = settings.CloseWindowBehavior;
		_startWithWindows = autostartManager.IsEnabled;
		_startMinimized = settings.StartMinimized;
		_checkForUpdates = settings.CheckForUpdates;
		_showRemoteControlNotifications = settings.ShowRemoteControlNotifications;

		Appearance = new AppearanceSettingsViewModel(settings, localization, themeService);
		Appearance.PropertyChanged += OnAppearancePropertyChanged;
		Favorites = new FavoritesSettingsViewModel(settings.FavoriteWorkItems);
		Pomodoro = new PomodoroSettingsViewModel(settings.Pomodoro);
		Plugins = new PluginSettingsViewModel(orchestrator, logger);

		SaveCommand = new AsyncRelayCommand(SaveAsync);
		CancelCommand = new RelayCommand(Cancel);
	}

	public AppearanceSettingsViewModel Appearance { get; }
	public FavoritesSettingsViewModel Favorites { get; }
	public PomodoroSettingsViewModel Pomodoro { get; }
	public PluginSettingsViewModel Plugins { get; }

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

	public bool ShowRemoteControlNotifications
	{
		get => _showRemoteControlNotifications;
		set => SetProperty(ref _showRemoteControlNotifications, value);
	}

	public string AppVersionDisplay => _localization.GetFormattedString("VersionFormat", Application.AppInfo.DisplayVersion);
	public string RuntimeVersion => $".NET {Environment.Version}";
	public string PlatformInfo => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

	public Action? CloseAction { get; set; }
	public bool DialogResult { get; set; }

	/// <summary>
	/// Why the last Save did not go through. A failed save leaves the dialog open, so without a
	/// message on it the button looks like it did nothing at all - the reason only reached the log.
	/// </summary>
	public string? SaveError
	{
		get => _saveError;
		private set
		{
			if (SetProperty(ref _saveError, value))
			{
				OnPropertyChanged(nameof(HasSaveError));
			}
		}
	}

	public bool HasSaveError => !string.IsNullOrEmpty(_saveError);

	public IAsyncRelayCommand SaveCommand { get; }
	public ICommand CancelCommand { get; }

	/// <summary>
	/// Covers all dismissal paths: Cancel and the titlebar X, which closes the window without going
	/// through the command.
	/// </summary>
	public void RevertPreview()
	{
		Appearance.RevertPreview();
	}

	/// <summary>
	/// The version line is computed from resources, so a live language switch has to refresh it too.
	/// </summary>
	private void OnAppearancePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.IsNullOrEmpty(e.PropertyName))
		{
			OnPropertyChanged(nameof(AppVersionDisplay));
		}
	}

	private async Task SaveAsync()
	{
		SaveError = null;

		try
		{
			var plugins = Plugins.Items.Select(p => p.ToEntry()).ToList();

			// A plugin with an invalid configuration would fail to initialize after the save, and
			// that failure only reaches the log. Refusing the save keeps the dialog open on it instead.
			var pluginErrors = await _orchestrator.ValidatePluginConfigurationsAsync(plugins, CancellationToken.None);
			if (pluginErrors.Count != 0)
			{
				SaveError = _localization.GetFormattedString("PluginConfigurationInvalid", string.Join(Environment.NewLine, pluginErrors));
				return;
			}

			var request = new SettingsSaveRequest
			{
				CloseWindowBehavior = CloseWindowBehavior,
				StartWithWindows = StartWithWindows,
				StartMinimized = StartMinimized,
				CheckForUpdates = CheckForUpdates,
				ShowRemoteControlNotifications = ShowRemoteControlNotifications,
				Theme = Appearance.SelectedTheme,
				FollowSystemTheme = Appearance.FollowSystemTheme,
				LightTheme = Appearance.SelectedLightTheme,
				DarkTheme = Appearance.SelectedDarkTheme,
				Language = Appearance.SelectedLanguage.Code,
				FavoriteWorkItems = Favorites.Items.ToList(),
				Plugins = plugins,
				Pomodoro = Pomodoro.ToSettings()
			};

			await _orchestrator.SaveSettingsAsync(request, CancellationToken.None);

			DialogResult = true;
			CloseAction?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to save settings");
			SaveError = _localization.GetFormattedString("SettingsSaveFailed", ex.Message);
			DialogResult = false;
		}
	}

	private void Cancel()
	{
		RevertPreview();
		DialogResult = false;
		CloseAction?.Invoke();
	}
}
