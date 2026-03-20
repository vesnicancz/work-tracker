using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Interfaces;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Repositories;

namespace WorkTracker.Infrastructure;

public static class DependencyInjection
{
	public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
	{
		// Database
		var dbPath = configuration.GetValue<string>("Database:Path");
		if (string.IsNullOrWhiteSpace(dbPath))
		{
			dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkTracker", "worktracker.db");
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

		// Domain Services - Transient (stateless, pure functions)
		services.AddTransient<IDateRangeService, DateRangeService>();
		services.AddTransient<IWorklogValidator, WorklogValidator>();

		// Plugin System
		services.AddSingleton<PluginManager>(serviceProvider =>
		{
			var logger = serviceProvider.GetRequiredService<ILogger<PluginManager>>();
			var pluginManager = new PluginManager(logger);

			// Add default plugin directory
			var pluginsPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"WorkTracker",
				"Plugins"
			);
			if (!Directory.Exists(pluginsPath))
			{
				Directory.CreateDirectory(pluginsPath);
			}
			pluginManager.AddPluginDirectory(pluginsPath);

			return pluginManager;
		});

		// Application Services - Transient (stateless, uses factory)
		services.AddTransient<IWorkEntryService, WorkEntryService>();

		// Register plugin-based submission service
		// This will replace the old WorklogSubmissionService
		services.AddTransient<IWorklogSubmissionService, PluginBasedWorklogSubmissionService>();

		return services;
	}

	public static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider)
	{
		var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await contextFactory.CreateDbContextAsync();
		await context.Database.MigrateAsync();
	}

	/// <summary>
	/// Initializes all loaded plugins with their configurations from user settings or appsettings.json.
	/// Note: Plugins must be loaded first by the application layer before calling this method.
	/// </summary>
	public static async Task InitializePluginsAsync(
		IServiceProvider serviceProvider,
		IConfiguration configuration,
		Dictionary<string, bool>? enabledPlugins = null,
		Dictionary<string, Dictionary<string, string>>? userPluginConfigurations = null)
	{
		var pluginManager = serviceProvider.GetRequiredService<PluginManager>();

		// Discover and load external plugins from plugin directory
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

			if (config.Any())
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
		await pluginManager.InitializePluginsAsync(pluginConfigs);
	}
}