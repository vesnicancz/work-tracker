using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Tests.Helpers;

/// <summary>
/// SQLite in-memory test fixture. Unlike <c>UseInMemoryDatabase</c>, SQLite supports real transactions,
/// which is required for tests that exercise <see cref="UnitOfWork"/> (explicit <c>BeginTransactionAsync</c>).
/// Keeps a single open connection for the fixture lifetime — SQLite destroys the in-memory DB as soon as
/// the last connection closes, so the connection must outlive any DbContext created from it.
/// </summary>
public sealed class SqliteInMemoryFixture : IAsyncDisposable
{
	private readonly SqliteConnection _connection;

	public DbContextOptions<WorkTrackerDbContext> Options { get; }
	public TestDbContextFactory ContextFactory { get; }

	public SqliteInMemoryFixture()
	{
		_connection = new SqliteConnection("DataSource=:memory:");
		_connection.Open();

		Options = new DbContextOptionsBuilder<WorkTrackerDbContext>()
			.UseSqlite(_connection)
			.Options;

		// Create schema once for this fixture
		using var context = new WorkTrackerDbContext(Options);
		context.Database.EnsureCreated();

		ContextFactory = new TestDbContextFactory(Options);
	}

	public WorkTrackerDbContext CreateVerificationContext() => new(Options);

	public async ValueTask DisposeAsync()
	{
		await _connection.DisposeAsync();
	}
}
