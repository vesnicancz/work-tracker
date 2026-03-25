using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkTracker.Infrastructure.Data;

namespace WorkTracker.Infrastructure.Tests.Data;

public class WorkTrackerDbContextFactoryTests
{
	[Fact]
	public void CreateDbContext_WithNoArgs_ShouldCreateContext()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();

		// Act
		using var context = factory.CreateDbContext(Array.Empty<string>());

		// Assert
		context.Should().NotBeNull();
		context.Should().BeOfType<WorkTrackerDbContext>();
	}

	[Fact]
	public void CreateDbContext_ShouldConfigureSqlite()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();

		// Act
		using var context = factory.CreateDbContext(Array.Empty<string>());

		// Assert
		var connectionString = context.Database.GetConnectionString();
		connectionString.Should().NotBeNull();
		connectionString.Should().Contain("Data Source=");
	}

	[Fact]
	public void CreateDbContext_ShouldUseDefaultDatabasePath()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();

		// Act
		using var context = factory.CreateDbContext(Array.Empty<string>());

		// Assert
		var connectionString = context.Database.GetConnectionString();
		connectionString.Should().Contain("worktracker.db");
	}

	[Fact]
	public void CreateDbContext_ShouldUseLocalApplicationDataFolder()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();

		// Act
		using var context = factory.CreateDbContext(Array.Empty<string>());

		// Assert
		var connectionString = context.Database.GetConnectionString();
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		connectionString.Should().Contain(localAppData);
	}

	[Fact]
	public void CreateDbContext_WithArguments_ShouldStillCreateContext()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();
		var args = new[] { "arg1", "arg2" };

		// Act
		using var context = factory.CreateDbContext(args);

		// Assert
		context.Should().NotBeNull();
	}

	[Fact]
	public void CreateDbContext_MultipleTimes_ShouldCreateSeparateContexts()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();

		// Act
		using var context1 = factory.CreateDbContext(Array.Empty<string>());
		using var context2 = factory.CreateDbContext(Array.Empty<string>());

		// Assert
		context1.Should().NotBeSameAs(context2);
	}

	[Fact]
	public void CreateDbContext_ShouldHaveWorkEntriesDbSet()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();

		// Act
		using var context = factory.CreateDbContext(Array.Empty<string>());

		// Assert
		context.WorkEntries.Should().NotBeNull();
	}

	[Fact]
	public void CreateDbContext_ShouldBeDisposable()
	{
		// Arrange
		var factory = new WorkTrackerDbContextFactory();
		var context = factory.CreateDbContext(Array.Empty<string>());

		// Act
		var act = () => context.Dispose();

		// Assert
		act.Should().NotThrow();
	}
}
