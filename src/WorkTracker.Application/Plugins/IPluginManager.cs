using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Plugins;

/// <summary>
/// Keeps the plugins that are enabled running on their configuration. Each running plugin is an instance
/// of its own, replaced by a new one when the configuration changes and shut down when it gets disabled,
/// so what a caller gets here never changes its configuration while it is being used.
/// </summary>
public interface IPluginManager : IAsyncDisposable
{
	/// <summary>
	/// Raised for each instance that has just started, on whatever thread started it.
	/// </summary>
	event EventHandler<IPlugin>? PluginStarted;

	/// <summary>
	/// Gets all loaded plugins, running or not
	/// </summary>
	IReadOnlyDictionary<string, IPluginDescriptor> LoadedPlugins { get; }

	/// <summary>
	/// Gets all loaded worklog upload plugins, running or not, including disabled ones
	/// </summary>
	IEnumerable<IWorklogUploadPluginDescriptor> AllWorklogUploadPlugins { get; }

	/// <summary>
	/// Gets the enabled worklog upload plugins, running or not. One that failed to start is still listed.
	/// </summary>
	IEnumerable<IWorklogUploadPluginDescriptor> EnabledWorklogUploadPlugins { get; }

	/// <summary>
	/// Gets the running worklog upload plugins
	/// </summary>
	IEnumerable<IWorklogUploadPlugin> WorklogUploadPlugins { get; }

	/// <summary>
	/// Gets the running status indicator plugins
	/// </summary>
	IEnumerable<IStatusIndicatorPlugin> StatusIndicatorPlugins { get; }

	/// <summary>
	/// Gets the running work suggestion plugins
	/// </summary>
	IEnumerable<IWorkSuggestionPlugin> WorkSuggestionPlugins { get; }

	/// <summary>
	/// Gets the running remote control plugins
	/// </summary>
	IEnumerable<IRemoteControlPlugin> RemoteControlPlugins { get; }

	/// <summary>
	/// Sets which plugins are enabled. The next <see cref="InitializePluginsAsync"/> starts and stops them accordingly.
	/// </summary>
	void SetEnabledPlugins(IEnumerable<string> pluginIds);

	/// <summary>
	/// Starts the enabled plugins that don't run yet or run on another configuration, and shuts down the
	/// disabled ones. A plugin that keeps its configuration is left alone.
	/// </summary>
	/// <returns>The plugins that could not be started. Details are in the log.</returns>
	Task<IReadOnlyList<PluginMetadata>> InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations, CancellationToken cancellationToken);

	/// <summary>
	/// Creates a new instance of a loaded plugin that the manager doesn't know about, for trying out a
	/// configuration without touching the running one. The caller shuts it down.
	/// </summary>
	IPlugin CreateDetachedInstance(IPluginDescriptor plugin);

	/// <summary>
	/// Shuts the running instance down for as long as the returned object isn't disposed, which starts a
	/// new one on the same configuration. For a plugin bound to something only one instance can use.
	/// </summary>
	Task<IAsyncDisposable> SuspendPluginAsync(string pluginId, CancellationToken cancellationToken);

	/// <summary>
	/// Gets a running plugin by its ID
	/// </summary>
	IPlugin? GetPlugin(string pluginId);

	/// <summary>
	/// Gets a running plugin of a specific type
	/// </summary>
	T? GetPlugin<T>(string pluginId) where T : class, IPlugin;
}
