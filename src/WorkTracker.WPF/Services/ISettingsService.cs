using WorkTracker.WPF.Models;

namespace WorkTracker.WPF.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public interface ISettingsService
{
	/// <summary>
	/// Load settings from storage
	/// </summary>
	ApplicationSettings LoadSettings();

	/// <summary>
	/// Save settings to storage
	/// </summary>
	void SaveSettings(ApplicationSettings settings);

	/// <summary>
	/// Get current settings
	/// </summary>
	ApplicationSettings Settings { get; }
}
