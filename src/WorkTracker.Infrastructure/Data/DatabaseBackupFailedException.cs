namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// The database could not be copied before an upgrade, so the upgrade did not run and nothing changed.
/// The database itself is reachable, unlike with <see cref="DatabaseUnavailableException"/>: what is in
/// the way is more likely a full disk or a path that is too long.
/// </summary>
public sealed class DatabaseBackupFailedException(string backupPath, Exception innerException)
	: Exception($"Could not back up the database to '{backupPath}' before upgrading it, so it was left as it was: {innerException.Message}", innerException)
{
	public string BackupPath { get; } = backupPath;
}
