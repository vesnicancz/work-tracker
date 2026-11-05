using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Orchestrators;

public class SettingsOrchestrator : ISettingsOrchestrator
{
	private readonly ISettingsService _settingsService;
	private readonly IPluginManager _pluginManager;
	private readonly ISecureStorage _secureStorage;
	private readonly IPluginConfigurationSource _configurationSource;
	private readonly IAutostartManager _autostartManager;
	private readonly ITrayIconService _trayIconService;
	private readonly IRemoteControlCoordinator _remoteControlCoordinator;
	private readonly IWorkSuggestionCache _suggestionCache;
	private readonly INotificationService _notificationService;
	private readonly ILocalizationService _localization;
	private readonly ILogger<SettingsOrchestrator> _logger;

	public SettingsOrchestrator(
		ISettingsService settingsService,
		IPluginManager pluginManager,
		ISecureStorage secureStorage,
		IPluginConfigurationSource configurationSource,
		IAutostartManager autostartManager,
		ITrayIconService trayIconService,
		IRemoteControlCoordinator remoteControlCoordinator,
		IWorkSuggestionCache suggestionCache,
		INotificationService notificationService,
		ILocalizationService localization,
		ILogger<SettingsOrchestrator> logger)
	{
		_remoteControlCoordinator = remoteControlCoordinator;
		_suggestionCache = suggestionCache;
		_notificationService = notificationService;
		_localization = localization;
		_settingsService = settingsService;
		_pluginManager = pluginManager;
		_secureStorage = secureStorage;
		_configurationSource = configurationSource;
		_autostartManager = autostartManager;
		_trayIconService = trayIconService;
		_logger = logger;
	}

	public List<PluginSettingsEntry> LoadPlugins()
	{
		var settings = _settingsService.Settings;
		var configurations = _configurationSource.Resolve(settings.PluginConfigurations);

		return _pluginManager.LoadedPlugins.Values
			.OrderBy(p => p.Metadata.Name)
			.Select(plugin => new PluginSettingsEntry(
				plugin,
				// Disabled by default — user must explicitly enable in Settings
				settings.EnabledPlugins.GetValueOrDefault(plugin.Metadata.Id),
				configurations.GetValueOrDefault(plugin.Metadata.Id) ?? []))
			.ToList();
	}

	public async Task<IReadOnlyList<string>> ValidatePluginConfigurationsAsync(IEnumerable<PluginSettingsEntry> plugins, CancellationToken cancellationToken)
	{
		// Only enabled plugins: a disabled one is never initialized, so a half-filled configuration
		// left on it must not block saving everything else.
		var errors = new List<string>();
		foreach (var plugin in plugins.Where(p => p.IsEnabled))
		{
			var result = await plugin.Plugin.ValidateConfigurationAsync(new Dictionary<string, string>(plugin.Configuration), cancellationToken);
			if (!result.IsValid)
			{
				errors.AddRange(result.Errors.Select(error => $"{plugin.Plugin.Metadata.Name}: {error}"));
			}
		}

		return errors;
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
		settings.ShowRemoteControlNotifications = request.ShowRemoteControlNotifications;
		settings.Theme = request.Theme ?? settings.Theme;
		settings.FollowSystemTheme = request.FollowSystemTheme;
		settings.LightTheme = request.LightTheme ?? settings.LightTheme;
		settings.DarkTheme = request.DarkTheme ?? settings.DarkTheme;
		// Normalize on the way in so only a valid code is ever written back to disk.
		settings.Language = LanguageCatalog.Normalize(request.Language ?? settings.Language);
		// Rebuilt from the request rather than merged into the clone, because the values there are
		// the decrypted ones and would go back to disk in the clear. That only covers the plugins
		// this instance loaded; SettingsService restores the entries of the ones it did not, still
		// protected, so a run that found no plugins cannot wipe their configuration.
		settings.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>();
		settings.EnabledPlugins = new Dictionary<string, bool>();
		settings.FavoriteWorkItems = request.FavoriteWorkItems;
		settings.Pomodoro = request.Pomodoro;

		// Save plugin configurations and enabled state, encrypting sensitive values
		foreach (var plugin in request.Plugins)
		{
			var config = new Dictionary<string, string>(plugin.Configuration);
			ProtectSensitiveFields(plugin.Plugin, config);
			settings.PluginConfigurations[plugin.Plugin.Metadata.Id] = config;
			settings.EnabledPlugins[plugin.Plugin.Metadata.Id] = plugin.IsEnabled;
		}

		await _settingsService.SaveSettingsAsync(settings, cancellationToken);

		// Update enabled plugins in PluginManager
		var enabledPluginIds = request.Plugins.Where(p => p.IsEnabled).Select(p => p.Plugin.Metadata.Id);
		_pluginManager.SetEnabledPlugins(enabledPluginIds);

		// Re-initialize plugins whose configuration changed
		var failedPlugins = await _pluginManager.InitializePluginsAsync(settings.PluginConfigurations, cancellationToken);
		_notificationService.ReportPluginFailures(_localization, failedPlugins);

		// What is cached came from the plugins as they were before this save
		_suggestionCache.Invalidate();

		// Connect remote control plugins that were just enabled, disconnect those just disabled
		await _remoteControlCoordinator.SyncAsync(cancellationToken);

		// Apply autostart setting
		_autostartManager.SetAutostart(request.StartWithWindows);

		// Refresh tray menu favorites
		_trayIconService.RefreshFavoritesMenu();

		_logger.LogInformation("Settings saved successfully");
	}

	public async Task<string> TestConnectionAsync(PluginSettingsEntry plugin, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		if (plugin.Plugin is not ITestablePlugin)
		{
			return "✗ Test connection not available for this plugin type";
		}

		_logger.LogInformation("Testing connection for plugin {PluginId}", plugin.Plugin.Metadata.Id);

		// Initialization below fails on an invalid configuration too, but only says that it failed.
		// Checking first lets the result name the field that is wrong.
		var validation = await plugin.Plugin.ValidateConfigurationAsync(new Dictionary<string, string>(plugin.Configuration), cancellationToken);
		if (!validation.IsValid)
		{
			return $"✗ {string.Join(Environment.NewLine, validation.Errors)}";
		}

		var tempConfig = new Dictionary<string, string>(plugin.Configuration);

		// A status indicator drives a device that only one instance can hold. It goes dark for the test
		// and comes back on its saved configuration, in the phase of a running Pomodoro.
		await using var suspended = plugin.Plugin is IStatusIndicatorPlugin
			? await _pluginManager.SuspendPluginAsync(plugin.Plugin.Metadata.Id, cancellationToken)
			: null;

		// The test runs on its own instance. The unsaved values never reach the running plugin, and an
		// upload that is under way can't end up on an account that was only being tried.
		IPlugin detached;
		try
		{
			detached = _pluginManager.CreateDetachedInstance(plugin.Plugin);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not create an instance of plugin {PluginId} for a connection test", plugin.Plugin.Metadata.Id);
			return $"✗ Connection failed: {ex.Message}";
		}

		try
		{
			return await InitializeAndTestAsync(detached, tempConfig, progress, cancellationToken);
		}
		finally
		{
			await ShutDownDetachedAsync(detached);
		}
	}

	private async Task<string> InitializeAndTestAsync(IPlugin instance, Dictionary<string, string> configuration, IProgress<string>? progress, CancellationToken cancellationToken)
	{
		bool initialized;
		try
		{
			initialized = await instance.InitializeAsync(configuration, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Initialization failed for plugin {PluginId} during connection test", instance.Metadata.Id);
			return $"✗ Connection failed: {ex.Message}";
		}

		if (!initialized)
		{
			_logger.LogWarning("Initialization failed for plugin {PluginId} during connection test", instance.Metadata.Id);
			return "✗ Connection failed: Unable to initialize plugin with current configuration";
		}

		var result = await ((ITestablePlugin)instance).TestConnectionAsync(progress, cancellationToken);

		if (result.IsSuccess)
		{
			_logger.LogInformation("Connection test successful for {PluginId}", instance.Metadata.Id);

			// The test can be what signed the user in, and a cached "not signed in" would outlive it
			_suggestionCache.Invalidate();

			return "✓ Connection successful";
		}

		_logger.LogWarning("Connection test failed for {PluginId}: {Error}", instance.Metadata.Id, result.Error);
		return $"✗ Connection failed: {result.Error}";
	}

	/// <summary>
	/// Runs in a finally block, so a failure is logged and must not replace the test's result.
	/// </summary>
	private async Task ShutDownDetachedAsync(IPlugin plugin)
	{
		try
		{
			await plugin.ShutdownAsync();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to shut down the instance of plugin {PluginId} used for a connection test", plugin.Metadata.Id);
		}
	}

	private void ProtectSensitiveFields(IPluginDescriptor plugin, Dictionary<string, string> config)
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
