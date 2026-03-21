namespace WorkTracker.Infrastructure;

/// <summary>
/// Default file system paths used by the application
/// </summary>
public static class WorkTrackerPaths
{
	public static string DefaultDatabasePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"WorkTracker",
		"worktracker.db");
}
