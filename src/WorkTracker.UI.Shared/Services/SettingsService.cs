using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public sealed class SettingsService : ISettingsService
{
	private readonly ILogger<SettingsService> _logger;
	private readonly string _settingsFilePath;
	private ApplicationSettings _settings;

	public SettingsService(ILogger<SettingsService> logger, IHostEnvironment hostEnvironment)
	{
		_logger = logger;

		// Store settings in AppData/Local/WorkTracker
		var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkTracker");

		if (!hostEnvironment.IsProduction())
		{
			appDataPath += $"_{hostEnvironment.EnvironmentName}";
		}

		Directory.CreateDirectory(appDataPath);
		_settingsFilePath = Path.Combine(appDataPath, "settings.json");

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
				return new ApplicationSettings();
			}

			var json = File.ReadAllText(_settingsFilePath);
			var settings = JsonSerializer.Deserialize<ApplicationSettings>(json);

			if (settings == null)
			{
				_logger.LogWarning("Failed to deserialize settings, using defaults");
				return new ApplicationSettings();
			}

			_logger.LogInformation("Settings loaded successfully");
			return settings;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error loading settings, using defaults");
			return new ApplicationSettings();
		}
	}

	public void SaveSettings(ApplicationSettings settings)
	{
		try
		{
			var options = new JsonSerializerOptions
			{
				WriteIndented = true
			};

			var json = JsonSerializer.Serialize(settings, options);
			File.WriteAllText(_settingsFilePath, json);

			_settings = settings;
			_logger.LogInformation("Settings saved successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error saving settings");
			throw;
		}
	}
}
