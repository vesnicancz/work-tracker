using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public class SettingsOrchestrator : ISettingsOrchestrator
{
	private readonly ISettingsService _settingsService;
	private readonly IPluginManager _pluginManager;
	private readonly ISecureStorage _secureStorage;
	private readonly IConfiguration _configuration;
	private readonly IAutostartManager _autostartManager;
	private readonly ITrayIconService _trayIconService;
	private readonly ILogger<SettingsOrchestrator> _logger;

	public SettingsOrchestrator(
		ISettingsService settingsService,
		IPluginManager pluginManager,
		ISecureStorage secureStorage,
		IConfiguration configuration,
		IAutostartManager autostartManager,
		ITrayIconService trayIconService,
		ILogger<SettingsOrchestrator> logger)
	{
		_settingsService = settingsService;
		_pluginManager = pluginManager;
		_secureStorage = secureStorage;
		_configuration = configuration;
		_autostartManager = autostartManager;
		_trayIconService = trayIconService;
		_logger = logger;
	}

	public List<PluginViewModel> LoadPlugins()
	{
		var plugins = new List<PluginViewModel>();

		foreach (var plugin in _pluginManager.LoadedPlugins.Values.OrderBy(p => p.Metadata.Name))
		{
			var pluginViewModel = new PluginViewModel(plugin);

			// Disabled by default — user must explicitly enable in Settings
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

			plugins.Add(pluginViewModel);
		}

		return plugins;
	}

	public async Task SaveSettingsAsync(SettingsSaveRequest request, CancellationToken cancellationToken)
	{
		// Start from a copy of what is stored rather than from a blank ApplicationSettings: the
		// dialog does not own every setting (LastSubmissionMode is written by the Submit dialog),
		// and anything it does not send back has to survive the save instead of resetting.
		var settings = _settingsService.Settings.Clone();

		settings.CloseWindowBehavior = request.CloseWindowBehavior;
		settings.StartWithWindows = request.StartWithWindows;
		settings.StartMinimized = request.StartMinimized;
		settings.CheckForUpdates = request.CheckForUpdates;
		settings.Theme = request.Theme ?? settings.Theme;
		settings.FollowSystemTheme = request.FollowSystemTheme;
		settings.LightTheme = request.LightTheme ?? settings.LightTheme;
		settings.DarkTheme = request.DarkTheme ?? settings.DarkTheme;
		// Normalize on the way in so only a valid code is ever written back to disk.
		settings.Language = LanguageCatalog.Normalize(request.Language ?? settings.Language);
		settings.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>();
		settings.EnabledPlugins = new Dictionary<string, bool>();
		settings.FavoriteWorkItems = request.FavoriteWorkItems;
		settings.Pomodoro = request.Pomodoro;

		// Save plugin configurations and enabled state, encrypting sensitive values
		foreach (var pluginVm in request.Plugins)
		{
			var config = new Dictionary<string, string>(pluginVm.Configuration);
			ProtectSensitiveFields(pluginVm.Plugin, config);
			settings.PluginConfigurations[pluginVm.Plugin.Metadata.Id] = config;
			settings.EnabledPlugins[pluginVm.Plugin.Metadata.Id] = pluginVm.IsEnabled;
		}

		await _settingsService.SaveSettingsAsync(settings, cancellationToken);

		// Update enabled plugins in PluginManager
		var enabledPluginIds = request.Plugins.Where(p => p.IsEnabled).Select(p => p.Plugin.Metadata.Id);
		_pluginManager.SetEnabledPlugins(enabledPluginIds);

		// Re-initialize plugins with new configuration
		await _pluginManager.InitializePluginsAsync(settings.PluginConfigurations, cancellationToken);

		// Apply autostart setting
		_autostartManager.SetAutostart(request.StartWithWindows);

		// Refresh tray menu favorites
		_trayIconService.RefreshFavoritesMenu();

		_logger.LogInformation("Settings saved successfully");
	}

	public async Task<string> TestConnectionAsync(PluginViewModel plugin, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		if (plugin.Plugin is not ITestablePlugin testablePlugin)
		{
			return "✗ Test connection not available for this plugin type";
		}

		_logger.LogInformation("Testing connection for plugin {PluginId}", plugin.Plugin.Metadata.Id);

		// The test runs against the live plugin instance, so it has to initialize it with the
		// dialog's unsaved values. Restore the stored configuration afterwards - otherwise a test
		// followed by Cancel would leave the running plugin on settings the user never saved.
		var tempConfig = new Dictionary<string, string>(plugin.Configuration);
		try
		{
			bool initialized;
			try
			{
				initialized = await plugin.Plugin.InitializeAsync(tempConfig, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Initialization failed for plugin {PluginId} during connection test", plugin.Plugin.Metadata.Id);
				return $"✗ Connection failed: {ex.Message}";
			}

			if (!initialized)
			{
				_logger.LogWarning("Initialization failed for plugin {PluginId} during connection test", plugin.Plugin.Metadata.Id);
				return "✗ Connection failed: Unable to initialize plugin with current configuration";
			}

			var result = await testablePlugin.TestConnectionAsync(progress, cancellationToken);

			if (result.IsSuccess)
			{
				_logger.LogInformation("Connection test successful for {PluginId}", plugin.Plugin.Metadata.Id);
				return "✓ Connection successful";
			}

			_logger.LogWarning("Connection test failed for {PluginId}: {Error}",
				plugin.Plugin.Metadata.Id, result.Error);
			return $"✗ Connection failed: {result.Error}";
		}
		finally
		{
			// Not the caller's token: a cancelled test still has to put the plugin back.
			await RestoreSavedConfigurationAsync(plugin.Plugin, CancellationToken.None);
		}
	}

	/// <summary>
	/// Puts a plugin back on its persisted configuration after a connection test. A plugin with no
	/// stored configuration yet is left alone - there is nothing to restore it to. Failures are
	/// logged and swallowed: this runs in a finally block and must not replace the test's result.
	/// </summary>
	private async Task RestoreSavedConfigurationAsync(IPlugin plugin, CancellationToken cancellationToken)
	{
		if (!_settingsService.Settings.PluginConfigurations.TryGetValue(plugin.Metadata.Id, out var savedConfig))
		{
			return;
		}

		try
		{
			await plugin.InitializeAsync(new Dictionary<string, string>(savedConfig), cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to restore saved configuration for plugin {PluginId} after a connection test", plugin.Metadata.Id);
		}
	}

	private void ProtectSensitiveFields(IPlugin plugin, Dictionary<string, string> config)
	{
		var pluginId = plugin.Metadata.Id;
		var passwordKeys = plugin.GetConfigurationFields()
			.Where(f => f.Type == PluginConfigurationFieldType.Password)
			.Select(f => f.Key)
			.ToHashSet();

		foreach (var key in passwordKeys)
		{
			if (config.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
			{
				config[key] = _secureStorage.Protect(value, pluginId, key);
			}
			else
			{
				_secureStorage.Remove(pluginId, key);
			}
		}
	}
}