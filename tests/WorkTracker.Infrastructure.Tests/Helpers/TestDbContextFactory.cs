using Microsoft.EntityFrameworkCore;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Tests.Helpers;

/// <summary>
/// In-memory implementation of <see cref="IDbContextFactory{TContext}"/> for repository tests.
/// Each instance should use a unique database name (e.g. <see cref="Guid.NewGuid"/>) to ensure test isolation.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<WorkTrackerDbContext>
{
	private readonly DbContextOptions<WorkTrackerDbContext> _options;

	public TestDbContextFactory(DbContextOptions<WorkTrackerDbContext> options)
	{
		_options = options;
	}

	public WorkTrackerDbContext CreateDbContext()
	{
		return new WorkTrackerDbContext(_options);
	}

	public Task<WorkTrackerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(CreateDbContext());
	}
}
