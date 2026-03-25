using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public class SettingsOrchestrator : ISettingsOrchestrator
{
	private readonly ISettingsService _settingsService;
	private readonly IPluginManager _pluginManager;
	private readonly IConfiguration _configuration;
	private readonly IAutostartManager _autostartManager;
	private readonly ITrayIconService _trayIconService;
	private readonly ILogger<SettingsOrchestrator> _logger;

	public SettingsOrchestrator(
		ISettingsService settingsService,
		IPluginManager pluginManager,
		IConfiguration configuration,
		IAutostartManager autostartManager,
		ITrayIconService trayIconService,
		ILogger<SettingsOrchestrator> logger)
	{
		_settingsService = settingsService;
		_pluginManager = pluginManager;
		_configuration = configuration;
		_autostartManager = autostartManager;
		_trayIconService = trayIconService;
		_logger = logger;
	}

	public List<PluginViewModel> LoadPlugins()
	{
		var plugins = new List<PluginViewModel>();

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

			plugins.Add(pluginViewModel);
		}

		return plugins;
	}

	public async Task SaveSettingsAsync(SettingsSaveRequest request)
	{
		var settings = new ApplicationSettings
		{
			CloseWindowBehavior = request.CloseWindowBehavior,
			StartWithWindows = request.StartWithWindows,
			StartMinimized = request.StartMinimized,
			Theme = request.Theme ?? _settingsService.Settings.Theme,
			PluginConfigurations = new Dictionary<string, Dictionary<string, string>>(),
			EnabledPlugins = new Dictionary<string, bool>(),
			FavoriteWorkItems = request.FavoriteWorkItems
		};

		// Save plugin configurations and enabled state
		foreach (var pluginVm in request.Plugins)
		{
			settings.PluginConfigurations[pluginVm.Plugin.Metadata.Id] =
				new Dictionary<string, string>(pluginVm.Configuration);
			settings.EnabledPlugins[pluginVm.Plugin.Metadata.Id] = pluginVm.IsEnabled;
		}

		await _settingsService.SaveSettingsAsync(settings);

		// Update enabled plugins in PluginManager
		var enabledPluginIds = request.Plugins.Where(p => p.IsEnabled).Select(p => p.Plugin.Metadata.Id);
		_pluginManager.SetEnabledPlugins(enabledPluginIds);

		// Re-initialize plugins with new configuration
		await _pluginManager.InitializePluginsAsync(settings.PluginConfigurations);

		// Apply autostart setting
		_autostartManager.SetAutostart(request.StartWithWindows);

		// Refresh tray menu favorites
		_trayIconService.RefreshFavoritesMenu();

		_logger.LogInformation("Settings saved successfully");
	}

	public async Task<string> TestConnectionAsync(PluginViewModel plugin)
	{
		// Test connection is only available for worklog upload plugins
		if (plugin.Plugin is not IWorklogUploadPlugin worklogPlugin)
		{
			return "✗ Test connection not available for this plugin type";
		}

		_logger.LogInformation("Testing connection for plugin {PluginId}", plugin.Plugin.Metadata.Id);

		// Temporarily initialize plugin with current configuration
		var tempConfig = new Dictionary<string, string>(plugin.Configuration);
		await plugin.Plugin.InitializeAsync(tempConfig);

		var result = await worklogPlugin.TestConnectionAsync();

		if (result.IsSuccess)
		{
			_logger.LogInformation("Connection test successful for {PluginId}", plugin.Plugin.Metadata.Id);
			return "✓ Connection successful";
		}

		_logger.LogWarning("Connection test failed for {PluginId}: {Error}",
			plugin.Plugin.Metadata.Id, result.Error);
		return $"✗ Connection failed: {result.Error}";
	}
}