using Microsoft.EntityFrameworkCore;
using WorkTracker.Domain.Entities;

namespace WorkTracker.Infrastructure.Data;

public class WorkTrackerDbContext : DbContext
{
	public WorkTrackerDbContext(DbContextOptions<WorkTrackerDbContext> options)
		: base(options)
	{
	}

	public DbSet<WorkEntry> WorkEntries { get; set; } = null!;

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<WorkEntry>(entity =>
		{
			entity.HasKey(e => e.Id);

			entity.Property(e => e.TicketId)
				.IsRequired(false)
				.HasMaxLength(100);

			entity.Property(e => e.StartTime)
				.IsRequired();

			entity.Property(e => e.EndTime);

			entity.Property(e => e.Description)
				.HasMaxLength(200);

			entity.Property(e => e.IsActive)
				.IsRequired();

			entity.Property(e => e.CreatedAt)
				.IsRequired();

			entity.Property(e => e.UpdatedAt);

			// Index for faster queries
			entity.HasIndex(e => e.StartTime);
			entity.HasIndex(e => e.IsActive);
		});
	}
}