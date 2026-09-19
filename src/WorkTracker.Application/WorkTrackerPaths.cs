namespace WorkTracker.Application;

/// <summary>
/// Centralized, environment-aware file system paths used by the application.
/// In non-Production environments (e.g. Development), paths include an environment suffix
/// (e.g. WorkTracker_Development) to isolate data from production.
/// </summary>
public static class WorkTrackerPaths
{
	private static readonly Lazy<string> _appDataDirectory = new(() => BuildAppDataDirectory(
		// DoNotVerify, not the default: on Unix the default option verifies the directory and
		// hands back an empty string when ~/.local/share does not exist yet — a fresh account, a
		// service user, a container. Path.Combine then turns "WorkTracker" into a relative path and
		// the database, the settings and the logs are created wherever the process happened to be
		// started from, a new empty set per working directory.
		Environment.GetFolderPath(
			Environment.SpecialFolder.LocalApplicationData,
			Environment.SpecialFolderOption.DoNotVerify),
		Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
			?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")));

	/// <summary>
	/// Builds the application data directory from the platform's local application data directory
	/// and the hosting environment name. Anything other than Production gets its own suffixed
	/// directory, so a Development run cannot touch the installed application's data.
	/// </summary>
	/// <remarks>
	/// The result is always rooted. Should the platform fail to name a local application data
	/// directory at all, the executable's own directory stands in: writing beside the binary is a
	/// poor home for user data, but it is at least one fixed place, where a relative path silently
	/// scatters a separate database per working directory.
	/// </remarks>
	internal static string BuildAppDataDirectory(string localApplicationData, string? environmentName)
	{
		var folder = "WorkTracker";

		if (!string.IsNullOrEmpty(environmentName)
			&& !environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase))
		{
			folder += $"_{environmentName}";
		}

		var root = string.IsNullOrWhiteSpace(localApplicationData)
			? AppContext.BaseDirectory
			: localApplicationData;

		return Path.Combine(root, folder);
	}

	/// <summary>
	/// Contents/ of the macOS .app bundle we are running from, or null anywhere else. A bundle is
	/// not just a folder with a different name: Contents/MacOS may hold nothing but signed code
	/// (a stray data file there fails <c>codesign --verify --strict</c> outright), and writing
	/// anywhere inside the bundle invalidates the signature that Apple Silicon insists on. So the
	/// two directories that AppContext.BaseDirectory would otherwise serve have to split apart.
	/// </summary>
	private static readonly Lazy<string?> _macAppBundleContents = new(() =>
	{
		if (!OperatingSystem.IsMacOS())
		{
			return null;
		}

		var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
		if (!string.Equals(Path.GetFileName(baseDirectory), "MacOS", StringComparison.Ordinal))
		{
			return null;
		}

		var contents = Path.GetDirectoryName(baseDirectory);
		return contents is not null && File.Exists(Path.Combine(contents, "Info.plist"))
			? contents
			: null;
	});

	private static readonly Lazy<string> _appContentDirectory = new(() =>
		_macAppBundleContents.Value is { } contents
			? Path.Combine(contents, "Resources")
			: AppContext.BaseDirectory);

	private static readonly Lazy<string> _writableBaseDirectory = new(() =>
		_macAppBundleContents.Value is not null
			? AppDataDirectory
			: AppContext.BaseDirectory);

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
		Path.Combine(WritableBaseDirectory, "plugins"));

	/// <summary>
	/// Root application data directory (e.g. %LocalAppData%\WorkTracker or %LocalAppData%\WorkTracker_Development).
	/// </summary>
	public static string AppDataDirectory => _appDataDirectory.Value;

	/// <summary>
	/// Directory holding the files shipped alongside the application (appsettings.json). Next to
	/// the executable everywhere except inside a macOS .app, where it is Contents/Resources.
	/// Read-only — see <see cref="WritableBaseDirectory"/> for anything the app creates.
	/// </summary>
	public static string AppContentDirectory => _appContentDirectory.Value;

	/// <summary>
	/// Base directory that relative, writable paths from configuration (plugin directories, a
	/// relative database path) resolve against. The executable directory everywhere except inside
	/// a macOS .app, where nothing may be written into the signed bundle and the app data
	/// directory takes over.
	/// </summary>
	public static string WritableBaseDirectory => _writableBaseDirectory.Value;

	/// <summary>
	/// Default SQLite database path.
	/// </summary>
	public static string DefaultDatabasePath => _defaultDatabasePath.Value;

	/// <summary>
	/// Default settings file path.
	/// </summary>
	public static string SettingsFilePath => _settingsFilePath.Value;

	/// <summary>
	/// Default log file path for GUI applications (Avalonia).
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
	/// Default plugins directory (relative to <see cref="WritableBaseDirectory"/>).
	/// </summary>
	public static string DefaultPluginsPath => _defaultPluginsPath.Value;

}
