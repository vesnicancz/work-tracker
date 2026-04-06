using System.Collections.ObjectModel;
using System.Net.Http;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Infrastructure.Auth;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Plugins;

/// <summary>
/// Manages plugin discovery, loading, and lifecycle
/// </summary>
public sealed class PluginManager : IPluginManager
{
	private readonly ILogger _logger;
	private readonly ServiceProvider _pluginServiceProvider;
	private readonly PluginLoader _loader;
	private readonly Lock _lock = new();

	private readonly Dictionary<string, IPlugin> _loadedPlugins = new();
	private readonly Dictionary<string, AssemblyLoadContext> _pluginContexts = new();
	private readonly List<string> _pluginDirectories = new();
	private readonly HashSet<string> _enabledPluginIds = new();

	public PluginManager(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
	{
		_logger = loggerFactory.CreateLogger<PluginManager>();
		_pluginServiceProvider = BuildPluginServiceProvider(loggerFactory, httpClientFactory);
		_loader = new PluginLoader(_pluginServiceProvider, loggerFactory.CreateLogger<PluginLoader>());
	}

	private static ServiceProvider BuildPluginServiceProvider(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
	{
		var services = new ServiceCollection();
		services.AddSingleton<ILoggerFactory>(_ => new NonDisposingLoggerFactory(loggerFactory));
		services.AddLogging();
		services.AddSingleton<IHttpClientFactory>(_ => new NonDisposingHttpClientFactory(httpClientFactory));
		services.AddSingleton<ITokenProviderFactory, MsalTokenProviderFactory>();
		return services.BuildServiceProvider();
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
	public async Task<int> DiscoverAndLoadPluginsAsync()
	{
		List<string> directoriesSnapshot;
		lock (_lock)
		{
			directoriesSnapshot = new List<string>(_pluginDirectories);
		}

		var pluginFiles = _loader.DiscoverPluginFiles(directoriesSnapshot);
		var loadedCount = 0;

		foreach (var pluginFile in pluginFiles)
		{
			try
			{
				if (await LoadPluginFromFileAsync(pluginFile))
				{
					loadedCount++;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to load plugin from {File}", pluginFile);
			}
		}

		_logger.LogInformation("Loaded {Count} plugins from {DirectoryCount} directories", loadedCount, directoriesSnapshot.Count);

		return loadedCount;
	}

	/// <summary>
	/// Loads a plugin from a specific file
	/// </summary>
	public async Task<bool> LoadPluginFromFileAsync(string assemblyPath)
	{
		if (!File.Exists(assemblyPath))
		{
			_logger.LogWarning("Plugin file not found: {Path}", assemblyPath);
			return false;
		}

		try
		{
			_logger.LogDebug("Loading plugin from {Path}", assemblyPath);

			var results = _loader.LoadFromFile(assemblyPath);

			if (results.Count == 0)
			{
				return false;
			}

			var anyRegistered = false;
			var skippedPlugins = new List<IPlugin>();

			foreach (var (plugin, context) in results)
			{
				var pluginId = plugin.Metadata.Id;

				lock (_lock)
				{
					if (_loadedPlugins.ContainsKey(pluginId))
					{
						_logger.LogWarning("Plugin {Id} is already loaded, skipping", pluginId);
						skippedPlugins.Add(plugin);
						continue;
					}

					_loadedPlugins[pluginId] = plugin;
					_pluginContexts[pluginId] = context;
				}

				anyRegistered = true;

				_logger.LogInformation("Loaded plugin: {Name} v{Version} by {Author}", plugin.Metadata.Name, plugin.Metadata.Version, plugin.Metadata.Author);
			}

			// Dispose skipped plugins and unload context if nothing was registered
			foreach (var skipped in skippedPlugins)
			{
				await skipped.DisposeAsync();
			}

			if (!anyRegistered && results.Count > 0)
			{
				results[0].Context.Unload();
			}

			return anyRegistered;
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
		where T : class, IPlugin
	{
		try
		{
			var plugin = _loader.LoadEmbedded<T>();
			var pluginId = plugin.Metadata.Id;

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
		HashSet<string> enabledSnapshot;
		lock (_lock)
		{
			pluginsSnapshot = new List<KeyValuePair<string, IPlugin>>(_loadedPlugins);
			enabledSnapshot = new HashSet<string>(_enabledPluginIds);
		}

		foreach (var kvp in pluginsSnapshot)
		{
			var pluginId = kvp.Key;
			var plugin = kvp.Value;

			if (!enabledSnapshot.Contains(pluginId))
			{
				_logger.LogDebug("Skipping initialization of disabled plugin: {Name}", plugin.Metadata.Name);
				continue;
			}

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
		await _pluginServiceProvider.DisposeAsync();
	}

	private sealed class NonDisposingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
	{
		public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
		public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);
		public void Dispose() { } // Host owns the lifetime
	}

	private sealed class NonDisposingHttpClientFactory(IHttpClientFactory inner) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => inner.CreateClient(name);
	}
}
