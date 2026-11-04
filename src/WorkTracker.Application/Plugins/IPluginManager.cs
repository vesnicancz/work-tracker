using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Plugins;

/// <summary>
/// Manages plugin discovery, loading, and lifecycle
/// </summary>
public interface IPluginManager : IAsyncDisposable
{
	/// <summary>
	/// Gets all loaded plugins
	/// </summary>
	IReadOnlyDictionary<string, IPlugin> LoadedPlugins { get; }

	/// <summary>
	/// Gets all loaded worklog upload plugins (unfiltered - includes disabled plugins)
	/// </summary>
	IEnumerable<IWorklogUploadPlugin> AllWorklogUploadPlugins { get; }

	/// <summary>
	/// Gets all enabled worklog upload plugins (filtered by enabled state)
	/// </summary>
	IEnumerable<IWorklogUploadPlugin> WorklogUploadPlugins { get; }


	/// <summary>
	/// Gets all enabled status indicator plugins (filtered by enabled state)
	/// </summary>
	IEnumerable<IStatusIndicatorPlugin> StatusIndicatorPlugins { get; }


	/// <summary>
	/// Gets all enabled work suggestion plugins (filtered by enabled state)
	/// </summary>
	IEnumerable<IWorkSuggestionPlugin> WorkSuggestionPlugins { get; }

	/// <summary>
	/// Gets all loaded remote control plugins (unfiltered - includes disabled plugins)
	/// </summary>
	IEnumerable<IRemoteControlPlugin> AllRemoteControlPlugins { get; }

	/// <summary>
	/// Gets all enabled remote control plugins (filtered by enabled state)
	/// </summary>
	IEnumerable<IRemoteControlPlugin> RemoteControlPlugins { get; }

	/// <summary>
	/// Sets which plugins are enabled
	/// </summary>
	void SetEnabledPlugins(IEnumerable<string> pluginIds);





	/// <summary>
	/// Initializes the enabled plugins whose configuration differs from what they already run on.
	/// </summary>
	/// <returns>The plugins that could not be started. Details are in the log.</returns>
	Task<IReadOnlyList<PluginMetadata>> InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations, CancellationToken cancellationToken);

	/// <summary>
	/// Creates a new instance of a loaded plugin that the manager doesn't know about, for trying out a
	/// configuration without touching the running one. The caller shuts it down.
	/// </summary>
	IPlugin CreateDetachedInstance(IPlugin plugin);

	/// <summary>
	/// Gets a plugin by its ID
	/// </summary>
	IPlugin? GetPlugin(string pluginId);

	/// <summary>
	/// Gets a plugin of a specific type
	/// </summary>
	T? GetPlugin<T>(string pluginId) where T : class, IPlugin;

}
