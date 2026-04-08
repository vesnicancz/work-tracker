using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WorkTracker.Application;

namespace WorkTracker.Infrastructure.Data;

public class WorkTrackerDbContextFactory : IDesignTimeDbContextFactory<WorkTrackerDbContext>
{
	public WorkTrackerDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<WorkTrackerDbContext>();
		optionsBuilder.UseSqlite($"Data Source={WorkTrackerPaths.DefaultDatabasePath}");

		return new WorkTrackerDbContext(optionsBuilder.Options);
	}
}
