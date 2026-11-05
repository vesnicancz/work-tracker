using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application;
using WorkTracker.Application.Interfaces;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Application.Settings;
using WorkTracker.Infrastructure.Settings;
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
		else
		{
			dbPath = dbPath.Trim();

			// Resolve relative paths against the writable base directory (consistent with plugin
			// directories); leave special SQLite data sources (":memory:", "file:" URIs) untouched
			if (!dbPath.StartsWith(':') && !dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(dbPath))
			{
				dbPath = Path.GetFullPath(Path.Combine(WorkTrackerPaths.WritableBaseDirectory, dbPath));
			}
		}

		// Pooling keeps the database file open for the whole application lifetime.
		// Disable it (Database:Pooling = false) when the database lives on a removable
		// drive, so the file handle is released after each operation and the drive
		// can be safely ejected.
		var pooling = configuration.GetValue<bool?>("Database:Pooling") ?? true;

		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = dbPath,
			Pooling = pooling,
		}.ToString();

		services.AddDbContextFactory<WorkTrackerDbContext>(options =>
			options.UseSqlite(connectionString));

		// Reads - Transient (stateless, uses factory). The repository for writes comes from the unit of work.
		services.AddTransient<IWorkEntryReader, WorkEntryReader>();

		// Unit of Work - Transient (stateless factory; each Create() produces a new transactional scope)
		services.AddTransient<IUnitOfWorkFactory, UnitOfWorkFactory>();

		services.AddSingleton<IWorkDataChangeDetector>(new DatabaseFileChangeDetector(dbPath));

		// Secure storage — secrets stored in native OS credential store
		// (Windows Credential Manager / macOS Keychain / Linux libsecret)
		services.AddSingleton<ISecureStorage, CredentialStoreSecureStorage>();
		services.AddSingleton<IPluginSettingsReader, PluginSettingsReader>();

		// HttpClient factory for plugins
		services.AddHttpClient();

		// Plugin System
		services.AddSingleton<PluginManager>(serviceProvider =>
		{
			var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
			var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
			var logger = loggerFactory.CreateLogger<PluginManager>();
			var pluginManager = new PluginManager(loggerFactory, httpClientFactory);

			// Load plugin directories from configuration, default to "plugins" subfolder next to executable
			// Relative paths are resolved against WorkTrackerPaths.WritableBaseDirectory — the exe
			// location, except inside a macOS .app, where the directory is created below and so
			// cannot sit in the signed bundle
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
					: Path.GetFullPath(Path.Combine(WorkTrackerPaths.WritableBaseDirectory, trimmedDir));

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
		services.AddSingleton<IPluginConfigurationSource>(new PluginConfigurationSource(configuration));

		return services;
	}
}
