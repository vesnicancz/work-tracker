using WorkTracker.Application.Interfaces;

namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// Watches the write time of the database file. A commit in rollback-journal mode writes to the file
/// itself, so that time moves with every change. Asking SQLite (<c>PRAGMA data_version</c>) would need
/// a connection kept open, and that would stop a database on a removable drive from being ejected.
/// </summary>
public sealed class DatabaseFileChangeDetector(string databasePath) : IWorkDataChangeDetector
{
	public DateTime? GetLastChangeUtc()
	{
		try
		{
			var file = new FileInfo(databasePath);
			return file.Exists ? file.LastWriteTimeUtc : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			return null;
		}
	}
}
