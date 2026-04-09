using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTracker.Application;
using WorkTracker.Application.Services;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public sealed class SettingsService : ISettingsService
{
	private readonly ILogger<SettingsService> _logger;
	private readonly ISecureStorage _secureStorage;
	private readonly string _settingsFilePath;
	private ApplicationSettings _settings;

	private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

	public SettingsService(ILogger<SettingsService> logger, ISecureStorage secureStorage, string? settingsDirectoryOverride = null)
	{
		_logger = logger;
		_secureStorage = secureStorage;

		var directory = string.IsNullOrWhiteSpace(settingsDirectoryOverride)
			? WorkTrackerPaths.AppDataDirectory
			: settingsDirectoryOverride;
		Directory.CreateDirectory(directory);
		_settingsFilePath = Path.Combine(directory, "settings.json");

		_settings = LoadSettings();
	}

	public ApplicationSettings Settings => _settings;

	public ApplicationSettings LoadSettings()
	{
		try
		{
			if (!File.Exists(_settingsFilePath))
			{
				_logger.LogInformation("Settings file not found, using defaults");
				_settings = new ApplicationSettings();
				return _settings;
			}

			var json = File.ReadAllText(_settingsFilePath);
			var settings = JsonSerializer.Deserialize<ApplicationSettings>(json);

			if (settings == null)
			{
				_logger.LogWarning("Failed to deserialize settings from {Path}, using defaults", _settingsFilePath);
				_settings = new ApplicationSettings();
				return _settings;
			}

			_logger.LogInformation("Settings loaded successfully");
			UnprotectPluginConfigurations(settings);
			_settings = settings;
			return _settings;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error loading settings, using defaults");
			_settings = new ApplicationSettings();
			return _settings;
		}
	}

	public async Task<ApplicationSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			if (!File.Exists(_settingsFilePath))
			{
				_logger.LogInformation("Settings file not found, using defaults");
				_settings = new ApplicationSettings();
				return _settings;
			}

			var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
			var settings = JsonSerializer.Deserialize<ApplicationSettings>(json);

			if (settings == null)
			{
				_logger.LogWarning("Failed to deserialize settings from {Path}, using defaults", _settingsFilePath);
				_settings = new ApplicationSettings();
				return _settings;
			}

			_logger.LogInformation("Settings loaded successfully");
			UnprotectPluginConfigurations(settings);
			_settings = settings;
			return _settings;
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error loading settings, using defaults");
			_settings = new ApplicationSettings();
			return _settings;
		}
	}

	/// <summary>
	/// Resolves any protected values in plugin configurations via the secure storage.
	/// Non-protected values pass through unchanged.
	/// </summary>
	private void UnprotectPluginConfigurations(ApplicationSettings settings)
	{
		if (settings.PluginConfigurations is null)
		{
			return;
		}

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

	public void SaveSettings(ApplicationSettings settings)
	{
		try
		{
			var json = JsonSerializer.Serialize(settings, WriteOptions);
			File.WriteAllText(_settingsFilePath, json);
			SetOwnerOnlyPermissions(_settingsFilePath);

			UnprotectPluginConfigurations(settings);
			_settings = settings;
			_logger.LogInformation("Settings saved successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error saving settings");
			throw;
		}
	}

	public async Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
	{
		try
		{
			var json = JsonSerializer.Serialize(settings, WriteOptions);
			await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
			SetOwnerOnlyPermissions(_settingsFilePath);

			UnprotectPluginConfigurations(settings);
			_settings = settings;
			_logger.LogInformation("Settings saved successfully");
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error saving settings");
			throw;
		}
	}

	private void SetOwnerOnlyPermissions(string filePath)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return;
		}

		try
		{
			File.SetUnixFileMode(filePath,
				UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to set file permissions on {Path}", filePath);
		}
	}
}
