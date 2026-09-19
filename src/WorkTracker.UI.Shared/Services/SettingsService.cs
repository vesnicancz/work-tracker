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
public sealed class SettingsService : ISettingsService, IDisposable
{
	private readonly ILogger<SettingsService> _logger;
	private readonly ISecureStorage _secureStorage;
	private readonly string _settingsFilePath;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private ApplicationSettings _settings;

	// The plugin state exactly as it sits in the file, still protected. A save only carries the
	// plugins the caller could see, so this is what the ones it could not see are restored from.
	private Dictionary<string, Dictionary<string, string>> _storedPluginConfigurations = new(StringComparer.Ordinal);
	private Dictionary<string, bool> _storedEnabledPlugins = new(StringComparer.Ordinal);

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
			CaptureStoredPluginState(settings);
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
			CaptureStoredPluginState(settings);
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
		// Serialize with the async path so concurrent sync + async callers don't race on the file.
		_writeLock.Wait();
		try
		{
			RestorePluginStateNobodySpokeFor(settings);

			var json = JsonSerializer.Serialize(settings, WriteOptions);
			WriteAtomically(json);

			CaptureStoredPluginState(settings);
			UnprotectPluginConfigurations(settings);
			_settings = settings;
			_logger.LogInformation("Settings saved successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error saving settings");
			throw;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public async Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
	{
		// ConfigureAwait(false) on both awaits: prevents deadlocks when a sync SaveSettings call
		// on a UI thread is waiting on _writeLock while this async path's continuations would
		// otherwise need to marshal back to that same (blocked) UI context.
		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			RestorePluginStateNobodySpokeFor(settings);

			var json = JsonSerializer.Serialize(settings, WriteOptions);
			await WriteAtomicallyAsync(json, cancellationToken).ConfigureAwait(false);

			CaptureStoredPluginState(settings);
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
		finally
		{
			_writeLock.Release();
		}
	}

	/// <summary>
	/// Remembers the plugin state in the shape the file holds it, protected values included, so a
	/// later save can put back whatever it was not told about.
	/// </summary>
	private void CaptureStoredPluginState(ApplicationSettings settings)
	{
		_storedPluginConfigurations = settings.PluginConfigurations is null
			? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
			: settings.PluginConfigurations.ToDictionary(
				pair => pair.Key,
				pair => pair.Value is null
					? new Dictionary<string, string>(StringComparer.Ordinal)
					: new Dictionary<string, string>(pair.Value, StringComparer.Ordinal),
				StringComparer.Ordinal);

		_storedEnabledPlugins = settings.EnabledPlugins is null
			? new Dictionary<string, bool>(StringComparer.Ordinal)
			: new Dictionary<string, bool>(settings.EnabledPlugins, StringComparer.Ordinal);
	}

	/// <summary>
	/// Puts back the configuration and enabled state of every plugin the incoming settings say
	/// nothing about.
	/// <para>
	/// A caller can only speak for the plugins it actually loaded. An instance that loaded none — a
	/// development build pointed at an empty plugins directory, a directory that was briefly
	/// unreachable — used to persist that emptiness, wiping the configuration of every plugin it
	/// could not see and deleting the matching secrets on the next save. Entries the caller did
	/// send still win, so disabling or reconfiguring a loaded plugin works as before; the rest are
	/// carried over exactly as they were read, still protected.
	/// </para>
	/// </summary>
	private void RestorePluginStateNobodySpokeFor(ApplicationSettings settings)
	{
		if (settings.PluginConfigurations is null)
		{
			settings.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
		}

		if (settings.EnabledPlugins is null)
		{
			settings.EnabledPlugins = new Dictionary<string, bool>(StringComparer.Ordinal);
		}

		foreach (var (pluginId, storedConfig) in _storedPluginConfigurations)
		{
			if (!settings.PluginConfigurations.ContainsKey(pluginId))
			{
				settings.PluginConfigurations[pluginId] = new Dictionary<string, string>(storedConfig, StringComparer.Ordinal);
			}
		}

		foreach (var (pluginId, storedEnabled) in _storedEnabledPlugins)
		{
			if (!settings.EnabledPlugins.ContainsKey(pluginId))
			{
				settings.EnabledPlugins[pluginId] = storedEnabled;
			}
		}
	}

	private void WriteAtomically(string json)
	{
		var tempPath = _settingsFilePath + ".tmp";
		File.WriteAllText(tempPath, json);
		SwapIn(tempPath);
	}

	private async Task WriteAtomicallyAsync(string json, CancellationToken cancellationToken)
	{
		var tempPath = _settingsFilePath + ".tmp";
		await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
		SwapIn(tempPath);
	}

	/// <summary>
	/// Swaps a fully written temporary file in for the settings file, keeping the previous contents
	/// as settings.json.bak. Writing in place risks leaving a truncated file behind if the process
	/// dies mid-save, and the backup is what makes an unwanted save recoverable by hand.
	/// </summary>
	private void SwapIn(string tempPath)
	{
		SetOwnerOnlyPermissions(tempPath);

		if (!File.Exists(_settingsFilePath))
		{
			File.Move(tempPath, _settingsFilePath);
			SetOwnerOnlyPermissions(_settingsFilePath);
			return;
		}

		var backupPath = _settingsFilePath + ".bak";

		try
		{
			File.Replace(tempPath, _settingsFilePath, backupPath, ignoreMetadataErrors: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			// Some file systems cannot do the three-way replace. The move below is still a rename,
			// so the settings file is never the half-written one; only the backup loses atomicity.
			_logger.LogWarning(ex, "Could not replace {Path} atomically, falling back to a copy and move", _settingsFilePath);
			File.Copy(_settingsFilePath, backupPath, overwrite: true);
			File.Move(tempPath, _settingsFilePath, overwrite: true);
		}

		SetOwnerOnlyPermissions(_settingsFilePath);
		SetOwnerOnlyPermissions(backupPath);
	}

	public void Dispose()
	{
		_writeLock.Dispose();
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
