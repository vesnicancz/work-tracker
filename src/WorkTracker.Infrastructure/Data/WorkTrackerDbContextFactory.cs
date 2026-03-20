using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkTracker.Infrastructure.Data;

public class WorkTrackerDbContextFactory : IDesignTimeDbContextFactory<WorkTrackerDbContext>
{
	public WorkTrackerDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<WorkTrackerDbContext>();

		// Use a default database path for migrations
		var dbPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"WorkTracker",
			"worktracker.db"
		);

		optionsBuilder.UseSqlite($"Data Source={dbPath}");

		return new WorkTrackerDbContext(optionsBuilder.Options);
	}
}
