using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WorkTracker.Infrastructure.Data;

/// <summary>
/// Gets the database ready for a run: the backup and the pending migrations, or a clear report that the
/// file is out of reach.
/// </summary>
public static class DatabaseInitializer
{
	// SQLite result codes that mean "the file is not reachable" rather than "the data is wrong".
	private const int SqliteReadOnly = 8;
	private const int SqliteIoError = 10;
	private const int SqliteCantOpen = 14;

	public static async Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
	{
		var contextFactory = serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

		EnsureDatabaseDirectoryExists(context.Database.GetDbConnection().DataSource);

		try
		{
			// Force rollback-journal mode. WAL is persisted in the database file and leaves
			// -wal/-shm side files on disk, which is fragile on removable exFAT drives
			// (no filesystem journaling, abrupt removal). DELETE mode is a no-op if already set.
			await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;", cancellationToken);

			// A failure here stops the startup before anything changes: no migration runs without the copy.
			await DatabaseMigrationBackup.CreateIfNeededAsync(context, cancellationToken);

			await context.Database.MigrateAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is not DatabaseBackupFailedException && IsDatabaseOutOfReach(ex))
		{
			var dataSource = context.Database.GetDbConnection().DataSource;
			throw new DatabaseUnavailableException(dataSource,
				$"Database '{dataSource}' could not be opened. " +
				"If the database is on a removable drive, make sure it is connected and mounted.", ex);
		}
	}

	private static void EnsureDatabaseDirectoryExists(string dbPath)
	{
		var dbDirectory = Path.GetDirectoryName(dbPath);
		if (string.IsNullOrEmpty(dbDirectory) || Directory.Exists(dbDirectory))
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(dbDirectory);
		}
		catch (Exception ex)
		{
			throw new DatabaseUnavailableException(dbPath,
				$"Configured database directory '{dbDirectory}' is not accessible. " +
				"If the database is on a removable drive, make sure it is connected and mounted.", ex);
		}
	}

	/// <summary>
	/// Distinguishes an unreachable database file (unplugged removable drive, disconnected
	/// network share, read-only media) from a genuine schema or data error. Only the former
	/// is worth retrying, so only the former is reported as <see cref="DatabaseUnavailableException"/>.
	/// </summary>
	private static bool IsDatabaseOutOfReach(Exception exception)
	{
		for (var ex = exception; ex != null; ex = ex.InnerException)
		{
			var outOfReach = ex switch
			{
				SqliteException sqlite => sqlite.SqliteErrorCode is SqliteCantOpen or SqliteIoError or SqliteReadOnly,
				// Covers DirectoryNotFoundException / FileNotFoundException / DriveNotFoundException
				IOException => true,
				UnauthorizedAccessException => true,
				_ => false,
			};

			if (outOfReach)
			{
				return true;
			}
		}

		return false;
	}
}
