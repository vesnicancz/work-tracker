using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Services;

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
	/// Load settings from storage asynchronously
	/// </summary>
	Task<ApplicationSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Save settings to storage
	/// </summary>
	void SaveSettings(ApplicationSettings settings);

	/// <summary>
	/// Save settings to storage asynchronously
	/// </summary>
	Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get current settings
	/// </summary>
	ApplicationSettings Settings { get; }
}
