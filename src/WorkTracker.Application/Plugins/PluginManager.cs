using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Plugins;

/// <summary>
/// Manages plugin discovery, loading, and lifecycle
/// </summary>
public class PluginManager : IDisposable
{
	private readonly ILogger<PluginManager> _logger;

	private readonly Dictionary<string, IPlugin> _loadedPlugins = new();
	private readonly Dictionary<string, AssemblyLoadContext> _pluginContexts = new();
	private readonly List<string> _pluginDirectories = new();
	private readonly HashSet<string> _enabledPluginIds = new();

	public PluginManager(ILogger<PluginManager> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Gets all loaded plugins
	/// </summary>
	public IReadOnlyDictionary<string, IPlugin> LoadedPlugins => _loadedPlugins;

	/// <summary>
	/// Gets all loaded worklog upload plugins (unfiltered - includes disabled plugins)
	/// </summary>
	public IEnumerable<IWorklogUploadPlugin> AllWorklogUploadPlugins =>
		_loadedPlugins.Values.OfType<IWorklogUploadPlugin>();

	/// <summary>
	/// Gets all enabled worklog upload plugins (filtered by enabled state)
	/// </summary>
	public IEnumerable<IWorklogUploadPlugin> WorklogUploadPlugins =>
		_loadedPlugins.Values
			.OfType<IWorklogUploadPlugin>()
			.Where(p => _enabledPluginIds.Contains(p.Metadata.Id));

	/// <summary>
	/// Sets which plugins are enabled
	/// </summary>
	public void SetEnabledPlugins(IEnumerable<string> pluginIds)
	{
		_enabledPluginIds.Clear();
		foreach (var id in pluginIds)
		{
			_enabledPluginIds.Add(id);
		}
		_logger.LogInformation("Updated enabled plugins: {PluginIds}", string.Join(", ", _enabledPluginIds));
	}

	/// <summary>
	/// Adds a directory to search for plugins
	/// </summary>
	public void AddPluginDirectory(string directory)
	{
		if (Directory.Exists(directory))
		{
			_pluginDirectories.Add(directory);
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
		var loadedCount = 0;

		foreach (var directory in _pluginDirectories)
		{
			var pluginFiles = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
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

		_logger.LogInformation("Loaded {Count} plugins from {DirectoryCount} directories", loadedCount, _pluginDirectories.Count);

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

			if (!pluginTypes.Any())
			{
				_logger.LogWarning("No plugin types found in {Assembly}", assemblyPath);
				return false;
			}

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

					if (_loadedPlugins.ContainsKey(pluginId))
					{
						_logger.LogWarning("Plugin {Id} is already loaded, skipping", pluginId);
						continue;
					}

					_loadedPlugins[pluginId] = plugin;
					_pluginContexts[pluginId] = context;

					_logger.LogInformation("Loaded plugin: {Name} v{Version} by {Author}", plugin.Metadata.Name, plugin.Metadata.Version, plugin.Metadata.Author);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to instantiate plugin type {Type}", pluginType.FullName);
				}
			}

			return true;
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

			if (_loadedPlugins.ContainsKey(pluginId))
			{
				_logger.LogWarning("Plugin {Id} is already loaded", pluginId);
				return false;
			}

			// Set logger for plugins that support it
			if (plugin is WorklogUploadPluginBase worklogPlugin)
			{
				worklogPlugin.SetLogger(_logger);
			}

			_loadedPlugins[pluginId] = plugin;

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
	public async Task InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations = null)
	{
		foreach (var kvp in _loadedPlugins)
		{
			var pluginId = kvp.Key;
			var plugin = kvp.Value;

			try
			{
				var config = configurations?.GetValueOrDefault(pluginId);
				var success = await plugin.InitializeAsync(config);

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
		return _loadedPlugins.GetValueOrDefault(pluginId);
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
		if (!_loadedPlugins.TryGetValue(pluginId, out var plugin))
		{
			return false;
		}

		try
		{
			await plugin.ShutdownAsync();
			_loadedPlugins.Remove(pluginId);

			if (_pluginContexts.TryGetValue(pluginId, out var context))
			{
				context.Unload();
				_pluginContexts.Remove(pluginId);
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
		foreach (var pluginId in _loadedPlugins.Keys.ToList())
		{
			await UnloadPluginAsync(pluginId);
		}
	}

	public void Dispose()
	{
		UnloadAllPluginsAsync()
			.GetAwaiter()
			.GetResult();
	}
}