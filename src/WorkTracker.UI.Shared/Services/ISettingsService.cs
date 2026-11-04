using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public interface ISettingsService
{
	/// <summary>
	/// Save settings to storage asynchronously
	/// </summary>
	Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get current settings
	/// </summary>
	ApplicationSettings Settings { get; }
}
