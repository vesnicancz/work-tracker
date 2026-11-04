using Microsoft.EntityFrameworkCore;

namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// Copies the database before migrations change it. A migration runs in a transaction, so one that
/// fails leaves nothing behind, but one that succeeds and turns out to have done the wrong thing to
/// someone's data could not be undone without this. A copy per migration, kept for the user to delete.
/// </summary>
internal static class DatabaseMigrationBackup
{
	/// <summary>
	/// Does nothing when no migration is pending, or for a database that holds nothing yet.
	/// </summary>
	/// <returns>The path of the copy, or null when none was made.</returns>
	public static async Task<string?> CreateIfNeededAsync(WorkTrackerDbContext context, CancellationToken cancellationToken)
	{
		var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
		if (pending.Count == 0)
		{
			return null;
		}

		var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
		if (!applied.Any())
		{
			return null;
		}

		var databasePath = context.Database.GetDbConnection().DataSource;
		if (!File.Exists(databasePath))
		{
			return null;
		}

		var backupPath = $"{databasePath}.before-{pending[0]}.bak";

		// From an earlier attempt, and a failed migration changed nothing since then
		if (File.Exists(backupPath))
		{
			return backupPath;
		}

		// VACUUM INTO rather than copying the file, which could catch the other process halfway through
		// a write. Through a temporary name, so that a backup path that exists is always a whole copy.
		// The name is short: SQLite can't open a path much longer than the database's own on Windows.
		var temporaryPath = $"{databasePath}.{Random.Shared.Next(0x10000):x4}.tmp";
		try
		{
			await context.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [temporaryPath], cancellationToken);
			File.Move(temporaryPath, backupPath, overwrite: true);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			throw new DatabaseBackupFailedException(backupPath, ex);
		}
		finally
		{
			File.Delete(temporaryPath);
		}

		return backupPath;
	}
}
