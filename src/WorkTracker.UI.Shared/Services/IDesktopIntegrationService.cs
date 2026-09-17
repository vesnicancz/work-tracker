namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Registers the application with the desktop environment (menu entry, icons) so its windows,
/// notifications and launcher show the right name and icon. A no-op on platforms that do not need it.
/// </summary>
public interface IDesktopIntegrationService
{
	Task EnsureInstalledAsync(CancellationToken cancellationToken = default);
}
