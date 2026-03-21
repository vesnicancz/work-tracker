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
	/// Sets which plugins are enabled
	/// </summary>
	void SetEnabledPlugins(IEnumerable<string> pluginIds);

	/// <summary>
	/// Adds a directory to search for plugins
	/// </summary>
	void AddPluginDirectory(string directory);

	/// <summary>
	/// Discovers and loads all plugins from registered directories
	/// </summary>
	int DiscoverAndLoadPlugins();

	/// <summary>
	/// Loads a plugin from a specific file
	/// </summary>
	bool LoadPluginFromFile(string assemblyPath);

	/// <summary>
	/// Loads an embedded plugin (plugin that's part of the main application)
	/// </summary>
	bool LoadEmbeddedPlugin<T>() where T : IPlugin, new();

	/// <summary>
	/// Initializes all loaded plugins with their configurations
	/// </summary>
	Task InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a plugin by its ID
	/// </summary>
	IPlugin? GetPlugin(string pluginId);

	/// <summary>
	/// Gets a plugin of a specific type
	/// </summary>
	T? GetPlugin<T>(string pluginId) where T : class, IPlugin;

	/// <summary>
	/// Unloads a specific plugin
	/// </summary>
	Task<bool> UnloadPluginAsync(string pluginId);

	/// <summary>
	/// Unloads all plugins
	/// </summary>
	Task UnloadAllPluginsAsync();
}
