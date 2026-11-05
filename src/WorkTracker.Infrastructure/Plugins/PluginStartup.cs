using Microsoft.Extensions.DependencyInjection;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Plugins;

public static class PluginStartup
{
	/// <summary>
	/// Loads the plugins and starts the enabled ones with their configurations.
	/// </summary>
	/// <param name="serviceProvider">The application's services.</param>
	/// <param name="enabledPlugins">The enabled state from user settings. A plugin not listed stays disabled.</param>
	/// <param name="userPluginConfigurations">The plugin configurations from user settings.</param>
	/// <param name="pluginsToStart">Which of the enabled plugins to start. It runs once the plugins are loaded and enabled, so it can ask the plugin manager.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The plugins that failed to start.</returns>
	public static async Task<IReadOnlyList<PluginMetadata>> StartAsync(
		IServiceProvider serviceProvider,
		IReadOnlyDictionary<string, bool> enabledPlugins,
		IReadOnlyDictionary<string, Dictionary<string, string>> userPluginConfigurations,
		Func<IPluginDescriptor, bool> pluginsToStart,
		CancellationToken cancellationToken)
	{
		var pluginManager = serviceProvider.GetRequiredService<PluginManager>();

		await pluginManager.DiscoverAndLoadPluginsAsync();

		pluginManager.SetEnabledPlugins(enabledPlugins.Where(kvp => kvp.Value).Select(kvp => kvp.Key));

		var configurations = serviceProvider
			.GetRequiredService<IPluginConfigurationSource>()
			.Resolve(userPluginConfigurations);

		return await pluginManager.InitializePluginsAsync(configurations, pluginsToStart, cancellationToken);
	}
}
