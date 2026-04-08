namespace WorkTracker.Application;

/// <summary>
/// Centralized, environment-aware file system paths used by the application.
/// In non-Production environments (e.g. Development), paths include an environment suffix
/// (e.g. WorkTracker_Development) to isolate data from production.
/// </summary>
public static class WorkTrackerPaths
{
	private static readonly Lazy<string> _appDataDirectory = new(() =>
	{
		var folder = "WorkTracker";
		var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
			?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

		if (!string.IsNullOrEmpty(env) && !env.Equals("Production", StringComparison.OrdinalIgnoreCase))
		{
			folder += $"_{env}";
		}

		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			folder);
	});

	private static readonly Lazy<string> _defaultDatabasePath = new(() =>
		Path.Combine(AppDataDirectory, "worktracker.db"));

	private static readonly Lazy<string> _settingsFilePath = new(() =>
		Path.Combine(AppDataDirectory, "settings.json"));

	private static readonly Lazy<string> _logFilePath = new(() =>
		Path.Combine(AppDataDirectory, "logs", "worktracker-.log"));

	private static readonly Lazy<string> _cliLogFilePath = new(() =>
		Path.Combine(AppDataDirectory, "logs", "worktracker-cli-.log"));

	private static readonly Lazy<string> _msalCacheDirectory = new(() =>
		Path.Combine(AppDataDirectory, "keys"));

	private static readonly Lazy<string> _defaultPluginsPath = new(() =>
		Path.Combine(AppContext.BaseDirectory, "plugins"));

	/// <summary>
	/// Root application data directory (e.g. %LocalAppData%\WorkTracker or %LocalAppData%\WorkTracker_Development).
	/// </summary>
	public static string AppDataDirectory => _appDataDirectory.Value;

	/// <summary>
	/// Default SQLite database path.
	/// </summary>
	public static string DefaultDatabasePath => _defaultDatabasePath.Value;

	/// <summary>
	/// Default settings file path.
	/// </summary>
	public static string SettingsFilePath => _settingsFilePath.Value;

	/// <summary>
	/// Default log file path for GUI applications (Avalonia, WPF).
	/// Serilog appends the date before the extension (e.g. worktracker-20260408.log).
	/// </summary>
	public static string LogFilePath => _logFilePath.Value;

	/// <summary>
	/// Default log file path for CLI application.
	/// </summary>
	public static string CliLogFilePath => _cliLogFilePath.Value;

	/// <summary>
	/// Directory for MSAL token cache files.
	/// </summary>
	public static string MsalCacheDirectory => _msalCacheDirectory.Value;

	/// <summary>
	/// Default plugins directory (relative to executable).
	/// </summary>
	public static string DefaultPluginsPath => _defaultPluginsPath.Value;

}
