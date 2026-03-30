using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Plugins;

/// <summary>
/// Manages plugin discovery, loading, and lifecycle
/// </summary>
public sealed class PluginManager : IPluginManager
{
	private readonly ILogger _logger;
	private readonly ILoggerFactory _loggerFactory;
	private readonly Lock _lock = new();

	private readonly Dictionary<string, IPlugin> _loadedPlugins = new();
	private readonly Dictionary<string, AssemblyLoadContext> _pluginContexts = new();
	private readonly List<string> _pluginDirectories = new();
	private readonly HashSet<string> _enabledPluginIds = new();

	public PluginManager(ILoggerFactory loggerFactory)
	{
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<PluginManager>();
	}

	/// <summary>
	/// Gets all loaded plugins
	/// </summary>
	public IReadOnlyDictionary<string, IPlugin> LoadedPlugins
	{
		get
		{
			lock (_lock)
			{
				return new ReadOnlyDictionary<string, IPlugin>(new Dictionary<string, IPlugin>(_loadedPlugins));
			}
		}
	}

	/// <summary>
	/// Gets all loaded worklog upload plugins (unfiltered - includes disabled plugins)
	/// </summary>
	public IEnumerable<IWorklogUploadPlugin> AllWorklogUploadPlugins => GetPlugins<IWorklogUploadPlugin>(enabledOnly: false);

	public IEnumerable<IWorklogUploadPlugin> WorklogUploadPlugins => GetPlugins<IWorklogUploadPlugin>(enabledOnly: true);

	public IEnumerable<IStatusIndicatorPlugin> AllStatusIndicatorPlugins => GetPlugins<IStatusIndicatorPlugin>(enabledOnly: false);

	public IEnumerable<IStatusIndicatorPlugin> StatusIndicatorPlugins => GetPlugins<IStatusIndicatorPlugin>(enabledOnly: true);

	public IEnumerable<IWorkSuggestionPlugin> AllWorkSuggestionPlugins => GetPlugins<IWorkSuggestionPlugin>(enabledOnly: false);

	public IEnumerable<IWorkSuggestionPlugin> WorkSuggestionPlugins => GetPlugins<IWorkSuggestionPlugin>(enabledOnly: true);

	private List<T> GetPlugins<T>(bool enabledOnly) where T : IPlugin
	{
		lock (_lock)
		{
			var plugins = _loadedPlugins.Values.OfType<T>();
			if (enabledOnly)
			{
				plugins = plugins.Where(p => _enabledPluginIds.Contains(p.Metadata.Id));
			}
			return plugins.ToList();
		}
	}

	/// <summary>
	/// Sets which plugins are enabled
	/// </summary>
	public void SetEnabledPlugins(IEnumerable<string> pluginIds)
	{
		lock (_lock)
		{
			_enabledPluginIds.Clear();
			foreach (var id in pluginIds)
			{
				_enabledPluginIds.Add(id);
			}
			_logger.LogInformation("Updated enabled plugins: {PluginIds}", string.Join(", ", _enabledPluginIds));
		}
	}

	/// <summary>
	/// Adds a directory to search for plugins
	/// </summary>
	public void AddPluginDirectory(string directory)
	{
		if (Directory.Exists(directory))
		{
			lock (_lock)
			{
				_pluginDirectories.Add(directory);
			}
			_logger.LogInformation("Added plugin directory: {Directory}", directory);
		}
		else
		{
			_logger.LogWarning("Plugin directory does not exist: {Directory}", directory);
		}
	}

	/// <summary>
	/// Discovers and loads all plugins from registered directories
	/// </summary>
	public int DiscoverAndLoadPlugins()
	{
		List<string> directoriesSnapshot;
		lock (_lock)
		{
			directoriesSnapshot = new List<string>(_pluginDirectories);
		}

		var loadedCount = 0;

		foreach (var directory in directoriesSnapshot)
		{
			var pluginFiles = Directory.GetFiles(directory, "WorkTracker.Plugin.*.dll", SearchOption.AllDirectories);
			foreach (var pluginFile in pluginFiles)
			{
				try
				{
					if (LoadPluginFromFile(pluginFile))
					{
						loadedCount++;
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to load plugin from {File}", pluginFile);
				}
			}
		}

		_logger.LogInformation("Loaded {Count} plugins from {DirectoryCount} directories", loadedCount, directoriesSnapshot.Count);

		return loadedCount;
	}

	/// <summary>
	/// Loads a plugin from a specific file
	/// </summary>
	public bool LoadPluginFromFile(string assemblyPath)
	{
		if (!File.Exists(assemblyPath))
		{
			_logger.LogWarning("Plugin file not found: {Path}", assemblyPath);
			return false;
		}

		try
		{
			_logger.LogDebug("Loading plugin from {Path}", assemblyPath);

			// Create isolated load context for the plugin
			var context = new PluginLoadContext(assemblyPath);
			var assembly = context.LoadFromAssemblyName(
				new AssemblyName(Path.GetFileNameWithoutExtension(assemblyPath))
			);

			// Find plugin types
			var pluginTypes = assembly.GetTypes()
				.Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
				.ToList();

			if (pluginTypes.Count == 0)
			{
				_logger.LogWarning("No plugin types found in {Assembly}", assemblyPath);
				context.Unload();
				return false;
			}

			var anyLoaded = false;

			foreach (var pluginType in pluginTypes)
			{
				try
				{
					var plugin = Activator.CreateInstance(pluginType) as IPlugin;
					if (plugin == null)
					{
						_logger.LogWarning("Failed to create instance of {Type}", pluginType.FullName);
						continue;
					}

					var pluginId = plugin.Metadata.Id;

					lock (_lock)
					{
						if (_loadedPlugins.ContainsKey(pluginId))
						{
							_logger.LogWarning("Plugin {Id} is already loaded, skipping", pluginId);
							continue;
						}

						_loadedPlugins[pluginId] = plugin;
						_pluginContexts[pluginId] = context;
					}

					if (plugin is PluginBase pluginBase)
					{
						pluginBase.SetLogger(CreatePluginLogger(pluginId));
					}

					anyLoaded = true;

					_logger.LogInformation("Loaded plugin: {Name} v{Version} by {Author}", plugin.Metadata.Name, plugin.Metadata.Version, plugin.Metadata.Author);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to instantiate plugin type {Type}", pluginType.FullName);
				}
			}

			if (!anyLoaded)
			{
				context.Unload();
			}

			return anyLoaded;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load plugin assembly from {Path}", assemblyPath);
			return false;
		}
	}

	/// <summary>
	/// Loads an embedded plugin (plugin that's part of the main application)
	/// </summary>
	public bool LoadEmbeddedPlugin<T>()
		where T : IPlugin, new()
	{
		try
		{
			var plugin = new T();
			var pluginId = plugin.Metadata.Id;

			if (plugin is PluginBase pluginBase)
			{
				pluginBase.SetLogger(CreatePluginLogger(pluginId));
			}

			lock (_lock)
			{
				if (_loadedPlugins.ContainsKey(pluginId))
				{
					_logger.LogWarning("Plugin {Id} is already loaded", pluginId);
					return false;
				}

				_loadedPlugins[pluginId] = plugin;
			}

			_logger.LogInformation("Loaded embedded plugin: {Name} v{Version} by {Author}", plugin.Metadata.Name, plugin.Metadata.Version, plugin.Metadata.Author);

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load embedded plugin {Type}", typeof(T).FullName);
			return false;
		}
	}

	/// <summary>
	/// Initializes all loaded plugins with their configurations
	/// </summary>
	public async Task InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations, CancellationToken cancellationToken)
	{
		List<KeyValuePair<string, IPlugin>> pluginsSnapshot;
		lock (_lock)
		{
			pluginsSnapshot = new List<KeyValuePair<string, IPlugin>>(_loadedPlugins);
		}

		foreach (var kvp in pluginsSnapshot)
		{
			var pluginId = kvp.Key;
			var plugin = kvp.Value;

			try
			{
				var config = configurations?.GetValueOrDefault(pluginId);
				var success = await plugin.InitializeAsync(config, cancellationToken);

				if (success)
				{
					_logger.LogInformation("Initialized plugin: {Name}", plugin.Metadata.Name);
				}
				else
				{
					_logger.LogWarning("Plugin initialization returned false: {Name}", plugin.Metadata.Name);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to initialize plugin: {Name}", plugin.Metadata.Name);
			}
		}
	}

	/// <summary>
	/// Gets a plugin by its ID
	/// </summary>
	public IPlugin? GetPlugin(string pluginId)
	{
		lock (_lock)
		{
			return _loadedPlugins.GetValueOrDefault(pluginId);
		}
	}

	/// <summary>
	/// Gets a plugin of a specific type
	/// </summary>
	public T? GetPlugin<T>(string pluginId)
		where T : class, IPlugin
	{
		return GetPlugin(pluginId) as T;
	}

	/// <summary>
	/// Unloads a specific plugin
	/// </summary>
	public async Task<bool> UnloadPluginAsync(string pluginId)
	{
		IPlugin? plugin;
		AssemblyLoadContext? context;

		lock (_lock)
		{
			if (!_loadedPlugins.TryGetValue(pluginId, out plugin))
			{
				return false;
			}

			_pluginContexts.TryGetValue(pluginId, out context);
		}

		try
		{
			await plugin.ShutdownAsync();

			lock (_lock)
			{
				_loadedPlugins.Remove(pluginId);
				_pluginContexts.Remove(pluginId);

				// Only unload the context if no other plugin shares it
				if (context != null && !_pluginContexts.ContainsValue(context))
				{
					context.Unload();
				}
			}

			_logger.LogInformation("Unloaded plugin: {Name}", plugin.Metadata.Name);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to unload plugin: {PluginId}", pluginId);
			return false;
		}
	}

	/// <summary>
	/// Unloads all plugins
	/// </summary>
	public async Task UnloadAllPluginsAsync()
	{
		List<string> pluginIds;
		lock (_lock)
		{
			pluginIds = _loadedPlugins.Keys.ToList();
		}

		foreach (var pluginId in pluginIds)
		{
			await UnloadPluginAsync(pluginId);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await UnloadAllPluginsAsync();
	}

	private ILogger CreatePluginLogger(string pluginId) =>
		_loggerFactory.CreateLogger($"WorkTracker.Plugin.{pluginId}");
}
