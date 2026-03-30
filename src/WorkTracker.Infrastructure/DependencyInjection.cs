using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Infrastructure.Plugins;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Repositories;
using WorkTracker.Infrastructure.Security;

namespace WorkTracker.Infrastructure;

public static class DependencyInjection
{
	public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
	{
		// Application layer services
		services.AddApplication();

		// Database
		var dbPath = configuration.GetValue<string>("Database:Path");
		if (string.IsNullOrWhiteSpace(dbPath))
		{
			dbPath = WorkTrackerPaths.DefaultDatabasePath;
		}

		var dbDirectory = Path.GetDirectoryName(dbPath);
		if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
		{
			Directory.CreateDirectory(dbDirectory);
		}

		services.AddDbContextFactory<WorkTrackerDbContext>(options =>
			options.UseSqlite($"Data Source={dbPath}"));

		// Repositories - Transient (stateless, uses factory)
		services.AddTransient<IWorkEntryRepository, WorkEntryRepository>();

		// Secure storage — secrets stored in native OS credential store
		// (Windows Credential Manager / macOS Keychain / Linux libsecret)
		services.AddSingleton<ISecureStorage, CredentialStoreSecureStorage>();

		// Plugin System
		services.AddSingleton<PluginManager>(serviceProvider =>
		{
			var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
			var logger = loggerFactory.CreateLogger<PluginManager>();
			var pluginManager = new PluginManager(loggerFactory);

			// Load plugin directories from configuration, default to "plugins" subfolder next to executable
			// Relative paths are resolved against AppContext.BaseDirectory (the exe location)
			var pluginDirs = configuration.GetSection("Plugins:Directories").Get<string[]>()
				?? [WorkTrackerPaths.DefaultPluginsPath];

			if (pluginDirs.All(string.IsNullOrWhiteSpace))
			{
				pluginDirs = [WorkTrackerPaths.DefaultPluginsPath];
			}

			foreach (var dir in pluginDirs)
			{
				if (string.IsNullOrWhiteSpace(dir))
				{
					continue;
				}

				var trimmedDir = dir.Trim();
				var resolvedDir = Path.IsPathRooted(trimmedDir)
					? trimmedDir
					: Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmedDir));

				try
				{
					Directory.CreateDirectory(resolvedDir);
					pluginManager.AddPluginDirectory(resolvedDir);
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Could not create plugins directory at {Path}", resolvedDir);
				}
			}

			return pluginManager;
		});
		services.AddSingleton<IPluginManager>(sp => sp.GetRequiredService<PluginManager>());

		return services;
	}

	public static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
	{
		var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
		await context.Database.MigrateAsync(cancellationToken);
	}

	/// <summary>
	/// Loads embedded and external plugins, then initializes them with configurations from user settings or appsettings.json.
	/// </summary>
	public static async Task InitializePluginsAsync(
		IServiceProvider serviceProvider,
		IConfiguration configuration,
		Dictionary<string, bool>? enabledPlugins = null,
		Dictionary<string, Dictionary<string, string>>? userPluginConfigurations = null,
		CancellationToken cancellationToken = default)
	{
		var pluginManager = serviceProvider.GetRequiredService<PluginManager>();

		// Discover and load plugins from plugin directory
		pluginManager.DiscoverAndLoadPlugins();

		// Set enabled plugins if provided, otherwise enable all plugins by default
		if (enabledPlugins != null && enabledPlugins.Count > 0)
		{
			var enabledPluginIds = enabledPlugins.Where(kvp => kvp.Value).Select(kvp => kvp.Key);
			pluginManager.SetEnabledPlugins(enabledPluginIds);
		}
		else
		{
			// First time setup - enable all loaded plugins by default
			var allPluginIds = pluginManager.LoadedPlugins.Keys;
			pluginManager.SetEnabledPlugins(allPluginIds);
		}

		// Load plugin configurations - prefer user settings over appsettings.json
		var pluginConfigs = new Dictionary<string, Dictionary<string, string>>();

		// First, load configurations from appsettings.json as fallback
		var pluginsSection = configuration.GetSection("Plugins");
		foreach (var pluginSection in pluginsSection.GetChildren())
		{
			var pluginId = pluginSection.Key;
			var config = new Dictionary<string, string>();

			foreach (var kvp in pluginSection.AsEnumerable(makePathsRelative: true))
			{
				if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
				{
					config[kvp.Key] = kvp.Value;
				}
			}

			if (config.Count != 0)
			{
				pluginConfigs[pluginId] = config;
			}
		}

		// Override with user-configured settings if available
		if (userPluginConfigurations != null)
		{
			foreach (var kvp in userPluginConfigurations)
			{
				pluginConfigs[kvp.Key] = kvp.Value;
			}
		}

		// Initialize plugins with their configurations
		await pluginManager.InitializePluginsAsync(pluginConfigs, cancellationToken);
	}
}