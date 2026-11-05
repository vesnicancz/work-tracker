using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Application.Services;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Tests.Data;

/// <summary>
/// What an upgrade does to a database the previous version left behind, built by the migrations of that
/// version and filled the way that version wrote rows. Goes through <see cref="DatabaseInitializer.InitializeAsync"/>,
/// the same way the applications start.
/// </summary>
public sealed class ExistingDatabaseMigrationTests : IAsyncDisposable
{
	private const string LastMigrationBeforeSingleActive = "20251103070714_Init";
	private const string EnforceSingleActiveEntry = "20261104215832_EnforceSingleActiveEntry";

	private readonly string _directory;
	private readonly string _databasePath;
	private readonly ServiceProvider _services;

	public ExistingDatabaseMigrationTests()
	{
		_directory = Path.Combine(Path.GetTempPath(), $"wt-migration-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_databasePath = Path.Combine(_directory, "worktracker.db");

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Database:Path"] = _databasePath,
				["Database:Pooling"] = "false"
			})
			.Build();

		_services = new ServiceCollection()
			.AddLogging()
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddInfrastructure(configuration)
			.BuildServiceProvider();
	}

	public async ValueTask DisposeAsync()
	{
		await _services.DisposeAsync();
		SqliteConnection.ClearAllPools();
		Directory.Delete(_directory, recursive: true);
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private async Task CreatePreviousVersionDatabaseAsync(params string[] rows)
	{
		var factory = _services.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await factory.CreateDbContextAsync(Ct);
		await context.Database.GetService<IMigrator>().MigrateAsync(LastMigrationBeforeSingleActive, Ct);

		foreach (var row in rows)
		{
			var insert = """INSERT INTO "WorkEntries" ("Id", "TicketId", "StartTime", "EndTime", "Description", "IsActive", "CreatedAt", "UpdatedAt") VALUES """ + row;
			await context.Database.ExecuteSqlRawAsync(insert, Ct);
		}
	}

	private Task UpgradeAsync() => DatabaseInitializer.InitializeAsync(_services, Ct);

	private sealed record StoredRow(long Id, string? TicketId, string StartTime, string? EndTime, string? Description, long IsActive, string CreatedAt, string? UpdatedAt);

	private async Task<Dictionary<long, StoredRow>> ReadRowsAsync(string? path = null)
	{
		await using var connection = new SqliteConnection($"Data Source={path ?? _databasePath};Pooling=False");
		await connection.OpenAsync(Ct);
		await using var command = connection.CreateCommand();
		command.CommandText = """SELECT "Id", "TicketId", "StartTime", "EndTime", "Description", "IsActive", "CreatedAt", "UpdatedAt" FROM "WorkEntries" """;

		var rows = new Dictionary<long, StoredRow>();
		await using var reader = await command.ExecuteReaderAsync(Ct);
		while (await reader.ReadAsync(Ct))
		{
			string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
			rows[reader.GetInt64(0)] = new StoredRow(reader.GetInt64(0), Text(1), reader.GetString(2), Text(3), Text(4), reader.GetInt64(5), reader.GetString(6), Text(7));
		}

		return rows;
	}

	private IWorkEntryService WorkEntryService() => _services.GetRequiredService<IWorkEntryService>();

	[Fact]
	public async Task Upgrade_LeavesOrdinaryRowsExactlyAsTheyWere()
	{
		var longDescription = new string('x', 800);
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'PROJ-1', '2026-03-02 09:00:00', '2026-03-02 10:00:00', 'finished', 0, '2026-03-02 09:00:00', '2026-03-02 10:00:00')",
			"(2, NULL, '2026-03-02 10:00:00', '2026-03-02 10:00:00', 'no ticket, zero length', 0, '2026-03-02 10:00:00', NULL)",
			"(3, 'PROJ-3', '2026-03-02 11:00:00.1234567', '2026-03-02 12:00:00.5', NULL, 0, '2026-03-02 11:00:00', NULL)",
			$"(4, 'PROJ-4', '2026-03-03 09:00:00', '2026-03-03 10:00:00', '{longDescription}', 0, '2026-03-03 09:00:00', NULL)",
			// What older versions could leave and today's rules wouldn't let in: overlapping rows, a row
			// with neither a ticket nor a description, an end before the start
			"(5, 'OLD-1', '2026-03-04 09:00:00', '2026-03-04 11:00:00', NULL, 0, '2026-03-04 09:00:00', NULL)",
			"(6, 'OLD-2', '2026-03-04 10:00:00', '2026-03-04 12:00:00', NULL, 0, '2026-03-04 10:00:00', NULL)",
			"(7, NULL, '2026-03-05 09:00:00', '2026-03-05 10:00:00', NULL, 0, '2026-03-05 09:00:00', NULL)",
			"(8, 'OLD-3', '2026-03-06 10:00:00', '2026-03-06 09:00:00', NULL, 0, '2026-03-06 10:00:00', NULL)",
			"(9, 'RUNNING', '2026-03-09 09:00:00', NULL, 'still running', 1, '2026-03-09 09:00:00', NULL)");
		var before = await ReadRowsAsync();

		await UpgradeAsync();

		(await ReadRowsAsync()).Should().BeEquivalentTo(before);
	}

	[Fact]
	public async Task Upgrade_OldRowsStayReadableThroughTheApplication()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, NULL, '2026-03-05 09:00:00', '2026-03-05 10:00:00', NULL, 0, '2026-03-05 09:00:00', NULL)",
			"(2, 'OLD-3', '2026-03-05 12:00:00', '2026-03-05 11:00:00', NULL, 0, '2026-03-05 12:00:00', NULL)",
			"(3, 'RUNNING', '2026-03-09 09:00:00', NULL, 'still running', 1, '2026-03-09 09:00:00', NULL)");

		await UpgradeAsync();

		var entries = await WorkEntryService().GetWorkEntriesByDateAsync(new DateTime(2026, 3, 5), Ct);
		entries.Should().HaveCount(2);
		(await WorkEntryService().GetActiveWorkAsync(Ct))!.TicketId.Should().Be("RUNNING");
	}

	[Fact]
	public async Task Upgrade_WithSeveralRunningEntries_KeepsTheNewestAndEndsTheOthersAtTheNextStart()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'OLDEST', '2026-03-02 09:00:00', NULL, NULL, 1, '2026-03-02 09:00:00', NULL)",
			"(2, 'BETWEEN', '2026-03-02 09:30:00', '2026-03-02 10:00:00', NULL, 0, '2026-03-02 09:30:00', NULL)",
			"(3, 'MIDDLE', '2026-03-02 11:00:00', NULL, NULL, 1, '2026-03-02 11:00:00', NULL)",
			"(4, 'NEWEST', '2026-03-02 12:00:00', NULL, NULL, 1, '2026-03-02 12:00:00', NULL)");

		await UpgradeAsync();

		var rows = await ReadRowsAsync();
		rows.Should().HaveCount(4);
		rows[1].EndTime.Should().Be("2026-03-02 09:30:00");
		rows[1].IsActive.Should().Be(0);
		rows[2].EndTime.Should().Be("2026-03-02 10:00:00");
		rows[3].EndTime.Should().Be("2026-03-02 12:00:00");
		rows[3].IsActive.Should().Be(0);
		rows[4].EndTime.Should().BeNull();
		rows[4].IsActive.Should().Be(1);
	}

	[Fact]
	public async Task Upgrade_WithTwoRunningEntriesFromTheSameMinute_KeepsTheLaterOne()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'FIRST', '2026-03-02 09:00:00', NULL, NULL, 1, '2026-03-02 09:00:00', NULL)",
			"(2, 'SECOND', '2026-03-02 09:00:00', NULL, NULL, 1, '2026-03-02 09:00:00', NULL)");

		await UpgradeAsync();

		var rows = await ReadRowsAsync();
		rows.Should().HaveCount(2);
		rows[1].EndTime.Should().Be("2026-03-02 09:00:00");
		rows[1].IsActive.Should().Be(0);
		rows[2].IsActive.Should().Be(1);
		rows[2].EndTime.Should().BeNull();
	}

	[Fact]
	public async Task Upgrade_WithFlagThatContradictsTheEnd_GoesByTheEnd()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'HAS-END', '2026-03-02 09:00:00', '2026-03-02 10:00:00', NULL, 1, '2026-03-02 09:00:00', NULL)",
			"(2, 'NO-END', '2026-03-02 11:00:00', NULL, NULL, 0, '2026-03-02 11:00:00', NULL)",
			"(3, 'RUNNING', '2026-03-02 12:00:00', NULL, NULL, 1, '2026-03-02 12:00:00', NULL)");

		await UpgradeAsync();

		var rows = await ReadRowsAsync();
		rows[1].IsActive.Should().Be(0);
		rows[1].EndTime.Should().Be("2026-03-02 10:00:00");
		rows[2].IsActive.Should().Be(0);
		rows[2].EndTime.Should().Be("2026-03-02 11:00:00");
		rows[3].IsActive.Should().Be(1);
		rows[3].EndTime.Should().BeNull();
	}

	[Fact]
	public async Task Upgrade_LeavesACopyOfTheDatabaseAsItWas()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'FIRST', '2026-03-02 09:00:00', NULL, NULL, 1, '2026-03-02 09:00:00', NULL)",
			"(2, 'SECOND', '2026-03-02 10:00:00', NULL, NULL, 1, '2026-03-02 10:00:00', NULL)");
		var before = await ReadRowsAsync();

		await UpgradeAsync();

		var backups = Directory.GetFiles(_directory, "*.bak");
		backups.Should().ContainSingle();
		Path.GetFileName(backups[0]).Should().Contain("EnforceSingleActiveEntry");
		(await ReadRowsAsync(backups[0])).Should().BeEquivalentTo(before);
		Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
	}

	[Fact]
	public async Task Upgrade_RunAgain_ChangesNothingAndMakesNoFurtherCopy()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'FIRST', '2026-03-02 09:00:00', NULL, NULL, 1, '2026-03-02 09:00:00', NULL)");

		await UpgradeAsync();
		var afterFirst = await ReadRowsAsync();
		await UpgradeAsync();

		(await ReadRowsAsync()).Should().BeEquivalentTo(afterFirst);
		Directory.GetFiles(_directory, "*.bak").Should().ContainSingle();
	}

	[Fact]
	public async Task FreshDatabase_GetsNoCopy()
	{
		await UpgradeAsync();

		File.Exists(_databasePath).Should().BeTrue();
		Directory.GetFiles(_directory, "*.bak").Should().BeEmpty();
	}

	[Fact]
	public async Task Upgrade_WhenTheCopyCannotBeMade_DoesNotMigrate()
	{
		await CreatePreviousVersionDatabaseAsync(
			"(1, 'FIRST', '2026-03-02 09:00:00', NULL, NULL, 1, '2026-03-02 09:00:00', NULL)",
			"(2, 'SECOND', '2026-03-02 10:00:00', NULL, NULL, 1, '2026-03-02 10:00:00', NULL)");
		var before = await ReadRowsAsync();

		// A directory where the copy has to go: it isn't a copy from an earlier attempt, and moving the
		// finished copy there fails.
		Directory.CreateDirectory($"{_databasePath}.before-{EnforceSingleActiveEntry}.bak");

		var act = UpgradeAsync;

		await act.Should().ThrowAsync<DatabaseBackupFailedException>();
		Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
		(await ReadRowsAsync()).Should().BeEquivalentTo(before);

		var factory = _services.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await factory.CreateDbContextAsync(Ct);
		(await context.Database.GetAppliedMigrationsAsync(Ct)).Should().NotContain(EnforceSingleActiveEntry);
	}

	[Fact]
	public async Task AfterUpgrade_ASecondRunningEntryIsRefusedByTheDatabase()
	{
		await UpgradeAsync();

		var factory = _services.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await factory.CreateDbContextAsync(Ct);
		var now = DateTime.Now;
		context.WorkEntries.Add(Domain.Entities.WorkEntry.Create("A-1", now.AddHours(-2), null, null, now));
		await context.SaveChangesAsync(Ct);

		context.WorkEntries.Add(Domain.Entities.WorkEntry.Create("B-1", now.AddHours(-1), null, null, now));
		var act = () => context.SaveChangesAsync(Ct);

		await act.Should().ThrowAsync<DbUpdateException>();
	}

	[Fact]
	public async Task AfterUpgrade_ConcurrentStartsNeverLeaveTwoRunningEntries()
	{
		await UpgradeAsync();

		const int runs = 25;
		for (var run = 0; run < runs; run++)
		{
			using var gate = new ManualResetEventSlim();
			var first = Task.Run(async () => { gate.Wait(Ct); return await WorkEntryService().StartWorkAsync("A-1", null, "a", null, Ct); }, Ct);
			var second = Task.Run(async () => { gate.Wait(Ct); return await WorkEntryService().StartWorkAsync("B-1", null, "b", null, Ct); }, Ct);
			gate.Set();

			var results = await Task.WhenAll(first, second);

			results.Should().Contain(r => r.IsSuccess);
			(await ReadRowsAsync()).Values.Count(r => r.IsActive != 0).Should().Be(1);

			var stopped = await WorkEntryService().StopWorkAsync(cancellationToken: Ct);
			stopped.IsSuccess.Should().BeTrue();

			var factory = _services.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
			await using var context = await factory.CreateDbContextAsync(Ct);
			await context.WorkEntries.ExecuteDeleteAsync(Ct);
		}
	}

	[Fact]
	public async Task Model_HasNoChangesWithoutAMigration()
	{
		var factory = _services.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var context = await factory.CreateDbContextAsync(Ct);

		context.Database.HasPendingModelChanges().Should().BeFalse();
	}
}
