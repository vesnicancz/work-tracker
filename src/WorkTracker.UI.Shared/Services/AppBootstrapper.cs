using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Shared application bootstrapping logic for UI hosts (Avalonia, WPF).
/// Call after building the host and before wiring up the main window.
/// </summary>
public static class AppBootstrapper
{
	/// <summary>
	/// Initializes database, plugins, and application state.
	/// The <paramref name="initializeDatabase"/> and <paramref name="initializePlugins"/> delegates
	/// are provided by the Infrastructure layer (DependencyInjection.InitializeDatabaseAsync / InitializePluginsAsync).
	/// </summary>
	public static async Task InitializeAsync(
		IServiceProvider services,
		Func<IServiceProvider, CancellationToken, Task> initializeDatabase,
		Func<IServiceProvider, IConfiguration, Dictionary<string, bool>?, Dictionary<string, Dictionary<string, string>>?, CancellationToken, Task> initializePlugins,
		CancellationToken cancellationToken = default)
	{
		// 1. Database
		await initializeDatabase(services, cancellationToken);

		// 2. Plugins
		var settingsService = services.GetRequiredService<ISettingsService>();
		var configuration = services.GetRequiredService<IConfiguration>();
		await initializePlugins(
			services, configuration,
			settingsService.Settings.EnabledPlugins,
			settingsService.Settings.PluginConfigurations,
			cancellationToken);

		// 3. Application state
		var worklogStateService = services.GetRequiredService<IWorklogStateService>();
		await worklogStateService.InitializeAsync();
	}
}
