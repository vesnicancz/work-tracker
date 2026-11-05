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

	// Starting and stopping take time and could overlap otherwise: the settings dialog can be saved while
	// the plugins are still starting up, and two instances of one plugin would end up running.
	private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

	private readonly Dictionary<string, LoadedPlugin> _plugins = new();
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

	public event EventHandler<IPlugin>? PluginStarted;

	/// <summary>
	/// Gets all loaded plugins
	/// </summary>
	public IReadOnlyDictionary<string, IPluginDescriptor> LoadedPlugins
	{
		get
		{
			lock (_lock)
			{
				return new ReadOnlyDictionary<string, IPluginDescriptor>(_plugins.ToDictionary(p => p.Key, p => (IPluginDescriptor)p.Value.Descriptor));
			}
		}
	}

	public IEnumerable<IWorklogUploadPluginDescriptor> AllWorklogUploadPlugins => GetUploadDescriptors(enabledOnly: false);

	public IEnumerable<IWorklogUploadPluginDescriptor> EnabledWorklogUploadPlugins => GetUploadDescriptors(enabledOnly: true);

	public IEnumerable<IWorklogUploadPlugin> WorklogUploadPlugins => GetRunning<IWorklogUploadPlugin>();

	public IEnumerable<IStatusIndicatorPlugin> StatusIndicatorPlugins => GetRunning<IStatusIndicatorPlugin>();

	public IEnumerable<IWorkSuggestionPlugin> WorkSuggestionPlugins => GetRunning<IWorkSuggestionPlugin>();

	public IEnumerable<IRemoteControlPlugin> RemoteControlPlugins => GetRunning<IRemoteControlPlugin>();

	private List<IWorklogUploadPluginDescriptor> GetUploadDescriptors(bool enabledOnly)
	{
		lock (_lock)
		{
			return _plugins
				.Where(p => !enabledOnly || _enabledPluginIds.Contains(p.Key))
				.Select(p => p.Value.Descriptor)
				.OfType<IWorklogUploadPluginDescriptor>()
				.ToList();
		}
	}

	private List<T> GetRunning<T>() where T : IPlugin
	{
		lock (_lock)
		{
			return _plugins
				.Where(p => _enabledPluginIds.Contains(p.Key))
				.Select(p => p.Value.Running)
				.OfType<T>()
				.ToList();
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
					if (_plugins.ContainsKey(pluginId))
					{
						_logger.LogWarning("Plugin {Id} is already loaded, skipping", pluginId);
						skippedPlugins.Add(plugin);
						continue;
					}

					_plugins[pluginId] = new LoadedPlugin(plugin, context);
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
	/// Registers a plugin type without loading it from a file. Tests use this for their own plugins.
	/// </summary>
	internal bool LoadEmbeddedPlugin<T>()
		where T : class, IPlugin
	{
		try
		{
			var plugin = _loader.LoadEmbedded<T>();
			var pluginId = plugin.Metadata.Id;

			lock (_lock)
			{
				if (_plugins.ContainsKey(pluginId))
				{
					_logger.LogWarning("Plugin {Id} is already loaded, skipping", pluginId);
					return false;
				}

				_plugins[pluginId] = new LoadedPlugin(plugin, null);
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

	public Task<IReadOnlyList<PluginMetadata>> InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations, CancellationToken cancellationToken)
	{
		return InitializePluginsAsync(configurations, _ => true, cancellationToken);
	}

	/// <summary>
	/// Starts only those enabled plugins that <paramref name="shouldStart"/> picks. The others stay
	/// listed with their metadata, without connecting to anything.
	/// </summary>
	public async Task<IReadOnlyList<PluginMetadata>> InitializePluginsAsync(Dictionary<string, Dictionary<string, string>>? configurations, Func<IPluginDescriptor, bool> shouldStart, CancellationToken cancellationToken)
	{
		var failed = new List<PluginMetadata>();

		await _lifecycleLock.WaitAsync(cancellationToken);
		try
		{
			List<KeyValuePair<string, LoadedPlugin>> pluginsSnapshot;
			HashSet<string> enabledSnapshot;
			lock (_lock)
			{
				pluginsSnapshot = _plugins.ToList();
				enabledSnapshot = new HashSet<string>(_enabledPluginIds);
			}

			foreach (var (pluginId, plugin) in pluginsSnapshot)
			{
				if (!enabledSnapshot.Contains(pluginId))
				{
					await StopRunningAsync(plugin);
					continue;
				}

				if (!shouldStart(plugin.Descriptor))
				{
					_logger.LogDebug("Skipping plugin not needed now: {Name}", plugin.Descriptor.Metadata.Name);
					continue;
				}

				var configuration = configurations?.GetValueOrDefault(pluginId);

				if (plugin.Running != null && IsSameConfiguration(plugin.RunningConfiguration, configuration))
				{
					_logger.LogDebug("Plugin {Name} already runs on this configuration", plugin.Descriptor.Metadata.Name);
					continue;
				}

				// A status indicator drives a device, and the new instance couldn't get to it while the old
				// one holds it. Anything else keeps running on the old configuration if the new one fails.
				if (plugin.Descriptor is IStatusIndicatorPlugin)
				{
					await StopRunningAsync(plugin);
				}

				if (!await StartAsync(plugin, configuration, cancellationToken))
				{
					failed.Add(plugin.Descriptor.Metadata);
				}
			}
		}
		finally
		{
			_lifecycleLock.Release();
		}

		return failed;
	}

	private static bool IsSameConfiguration(Dictionary<string, string>? running, Dictionary<string, string>? requested)
	{
		var current = running ?? [];
		var other = requested ?? [];
		return current.Count == other.Count
			&& current.All(pair => other.TryGetValue(pair.Key, out var value) && value == pair.Value);
	}

	/// <summary>
	/// Starts a new instance and puts it in place of the running one, if it starts.
	/// </summary>
	private async Task<bool> StartAsync(LoadedPlugin plugin, Dictionary<string, string>? configuration, CancellationToken cancellationToken)
	{
		var name = plugin.Descriptor.Metadata.Name;
		IPlugin? instance = null;

		try
		{
			instance = _loader.CreateInstance(plugin.Descriptor.GetType());

			var started = await instance.InitializeAsync(configuration == null ? [] : new Dictionary<string, string>(configuration), cancellationToken);
			if (!started)
			{
				_logger.LogWarning("Plugin initialization returned false: {Name}", name);
				await ShutDownAsync(instance, name);
				return false;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			if (instance != null)
			{
				await ShutDownAsync(instance, name);
			}

			throw;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to initialize plugin: {Name}", name);
			if (instance != null)
			{
				await ShutDownAsync(instance, name);
			}

			return false;
		}

		IPlugin? previous;
		lock (_lock)
		{
			previous = plugin.Running;
			plugin.Running = instance;
			plugin.RunningConfiguration = configuration;
		}

		if (previous != null)
		{
			await ShutDownAsync(previous, name);
		}

		_logger.LogInformation("Initialized plugin: {Name}", name);
		RaisePluginStarted(instance);

		return true;
	}

	private void RaisePluginStarted(IPlugin instance)
	{
		try
		{
			PluginStarted?.Invoke(this, instance);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "A subscriber failed while handling the start of plugin {Name}", instance.Metadata.Name);
		}
	}

	private async Task StopRunningAsync(LoadedPlugin plugin)
	{
		IPlugin? running;
		lock (_lock)
		{
			running = plugin.Running;
			plugin.Running = null;
			plugin.RunningConfiguration = null;
		}

		if (running != null)
		{
			await ShutDownAsync(running, plugin.Descriptor.Metadata.Name);
		}
	}

	/// <summary>
	/// A plugin that fails to shut down must not get in the way of the others or of what comes next.
	/// </summary>
	private async Task ShutDownAsync(IPlugin instance, string name)
	{
		try
		{
			await instance.ShutdownAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to shut down plugin: {Name}", name);
		}
	}

	public IPlugin CreateDetachedInstance(IPluginDescriptor plugin)
	{
		return _loader.CreateInstance(plugin.GetType());
	}

	public async Task<IAsyncDisposable> SuspendPluginAsync(string pluginId, CancellationToken cancellationToken)
	{
		await _lifecycleLock.WaitAsync(cancellationToken);
		try
		{
			LoadedPlugin? plugin;
			Dictionary<string, string>? configuration;
			lock (_lock)
			{
				if (!_plugins.TryGetValue(pluginId, out plugin) || plugin.Running == null)
				{
					return new Suspension(null);
				}

				configuration = plugin.RunningConfiguration;
			}

			await StopRunningAsync(plugin);

			return new Suspension(() => ResumeAsync(plugin, pluginId, configuration));
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	private async Task ResumeAsync(LoadedPlugin plugin, string pluginId, Dictionary<string, string>? configuration)
	{
		await _lifecycleLock.WaitAsync(CancellationToken.None);
		try
		{
			bool isWanted;
			lock (_lock)
			{
				// Settings saved in the meantime have already decided what should run
				isWanted = plugin.Running == null && _enabledPluginIds.Contains(pluginId) && _plugins.ContainsKey(pluginId);
			}

			if (isWanted)
			{
				await StartAsync(plugin, configuration, CancellationToken.None);
			}
		}
		finally
		{
			_lifecycleLock.Release();
		}
	}

	/// <summary>
	/// Gets a running plugin by its ID
	/// </summary>
	public IPlugin? GetPlugin(string pluginId)
	{
		lock (_lock)
		{
			return _enabledPluginIds.Contains(pluginId) ? _plugins.GetValueOrDefault(pluginId)?.Running : null;
		}
	}

	/// <summary>
	/// Gets a running plugin of a specific type
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
		await _lifecycleLock.WaitAsync();
		try
		{
			LoadedPlugin? plugin;
			lock (_lock)
			{
				if (!_plugins.TryGetValue(pluginId, out plugin))
				{
					return false;
				}
			}

			await StopRunningAsync(plugin);
			await ShutDownAsync(plugin.Descriptor, plugin.Descriptor.Metadata.Name);

			lock (_lock)
			{
				_plugins.Remove(pluginId);

				// Only unload the context if no other plugin shares it
				if (plugin.Context != null && _plugins.Values.All(p => p.Context != plugin.Context))
				{
					plugin.Context.Unload();
				}
			}

			_logger.LogInformation("Unloaded plugin: {Name}", plugin.Descriptor.Metadata.Name);
			return true;
		}
		finally
		{
			_lifecycleLock.Release();
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
			pluginIds = _plugins.Keys.ToList();
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

	/// <param name="descriptor">An instance that never gets initialized, for what can be asked without starting the plugin.</param>
	/// <param name="context">Where the plugin was loaded into. Null for one that wasn't loaded from a file.</param>
	private sealed class LoadedPlugin(IPlugin descriptor, AssemblyLoadContext? context)
	{
		public IPlugin Descriptor { get; } = descriptor;

		public AssemblyLoadContext? Context { get; } = context;

		public IPlugin? Running { get; set; }

		public Dictionary<string, string>? RunningConfiguration { get; set; }
	}

	private sealed class Suspension(Func<Task>? resume) : IAsyncDisposable
	{
		private Func<Task>? _resume = resume;

		public async ValueTask DisposeAsync()
		{
			var resume = Interlocked.Exchange(ref _resume, null);
			if (resume != null)
			{
				await resume();
			}
		}
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
