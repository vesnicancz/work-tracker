namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Checks GitHub releases for a newer application version.
/// </summary>
public interface IUpdateCheckService
{
	/// <summary>
	/// Checks whether a newer version is available and shows a system notification if so.
	/// This method is safe to call fire-and-forget; it silently handles all errors.
	/// </summary>
	Task CheckForUpdateAsync(CancellationToken cancellationToken = default);
}