using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Application;
using WorkTracker.Application.Services;
using WorkTracker.Application.Settings;

namespace WorkTracker.Infrastructure.Settings;

/// <summary>
/// Reads <see cref="PluginSettings"/> from the settings file the GUI writes.
/// Deserializing into the slice type ignores every other setting, so the CLI never has to know
/// the full settings model.
/// </summary>
public sealed class PluginSettingsReader : IPluginSettingsReader
{
	private readonly ISecureStorage _secureStorage;
	private readonly ILogger<PluginSettingsReader> _logger;
	private readonly string _settingsFilePath;

	public PluginSettingsReader(
		ISecureStorage secureStorage,
		ILogger<PluginSettingsReader> logger,
		string? settingsFilePathOverride = null)
	{
		_secureStorage = secureStorage;
		_logger = logger;
		_settingsFilePath = settingsFilePathOverride ?? WorkTrackerPaths.SettingsFilePath;
	}

	public async Task<PluginSettings> ReadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			if (!File.Exists(_settingsFilePath))
			{
				_logger.LogInformation(
					"Settings file {Path} not found; no plugins will be enabled", _settingsFilePath);
				return new PluginSettings();
			}

			var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
			var settings = JsonSerializer.Deserialize<PluginSettings>(json);

			if (settings == null)
			{
				_logger.LogWarning("Failed to deserialize plugin settings from {Path}", _settingsFilePath);
				return new PluginSettings();
			}

			Unprotect(settings);

			_logger.LogInformation(
				"Loaded plugin settings: {EnabledCount} of {ConfiguredCount} plugins enabled",
				settings.EnabledPlugins.Count(kvp => kvp.Value),
				settings.EnabledPlugins.Count);

			return settings;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Plugins are optional; a broken settings file must not stop the command from running.
			_logger.LogError(ex, "Error reading plugin settings from {Path}", _settingsFilePath);
			return new PluginSettings();
		}
	}

	/// <summary>
	/// Resolves protected values through the secure storage, mirroring what the GUI does on load.
	/// Without this the plugins would receive ciphertext instead of their credentials.
	/// </summary>
	private void Unprotect(PluginSettings settings)
	{
		foreach (var pluginConfig in settings.PluginConfigurations.Values)
		{
			if (pluginConfig is null)
			{
				continue;
			}

			foreach (var key in pluginConfig.Keys.ToList())
			{
				pluginConfig[key] = _secureStorage.Unprotect(pluginConfig[key]);
			}
		}
	}
}
