namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// Thrown when the configured SQLite database cannot be reached — typically because it lives
/// on a removable drive that is not currently connected. Unlike other startup failures this
/// one is recoverable: the presentation layer can offer the user a retry once the drive is
/// plugged in, without restarting the application.
/// </summary>
public sealed class DatabaseUnavailableException : Exception
{
	public DatabaseUnavailableException(string databasePath, string message, Exception? innerException)
		: base(message, innerException)
	{
		DatabasePath = databasePath;
	}

	/// <summary>Configured location of the database that could not be opened.</summary>
	public string DatabasePath { get; }
}
