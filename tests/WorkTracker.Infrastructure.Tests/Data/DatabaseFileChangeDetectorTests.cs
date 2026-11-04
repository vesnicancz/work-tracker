using FluentAssertions;
using Microsoft.Data.Sqlite;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Tests.Data;

public sealed class DatabaseFileChangeDetectorTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("worktracker-detector-").FullName;

	private string DatabasePath => Path.Combine(_directory, "test.db");

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public void GetLastChangeUtc_WithoutTheFile_ReturnsNull()
	{
		new DatabaseFileChangeDetector(DatabasePath).GetLastChangeUtc().Should().BeNull();
	}

	[Theory]
	[InlineData(":memory:")]
	[InlineData("file:test.db?mode=memory")]
	public void GetLastChangeUtc_WithSpecialDataSource_ReturnsNull(string dataSource)
	{
		new DatabaseFileChangeDetector(dataSource).GetLastChangeUtc().Should().BeNull();
	}

	/// <summary>
	/// The real situation: the GUI keeps its pooled connection open while the CLI commits and exits.
	/// </summary>
	[Fact]
	public async Task GetLastChangeUtc_AfterACommitFromAnotherConnection_Moves()
	{
		var connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
		await using var heldOpen = new SqliteConnection(connectionString);
		await heldOpen.OpenAsync(TestContext.Current.CancellationToken);
		await ExecuteAsync(heldOpen, "PRAGMA journal_mode=DELETE; CREATE TABLE T (V INTEGER);");

		var detector = new DatabaseFileChangeDetector(DatabasePath);
		var before = detector.GetLastChangeUtc();
		await Task.Delay(50, TestContext.Current.CancellationToken);

		await using (var other = new SqliteConnection(connectionString + ";Pooling=False"))
		{
			await other.OpenAsync(TestContext.Current.CancellationToken);
			await ExecuteAsync(other, "INSERT INTO T VALUES (1);");
		}

		before.Should().NotBeNull();
		detector.GetLastChangeUtc().Should().BeAfter(before!.Value);
	}

	private static async Task ExecuteAsync(SqliteConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
	}
}
