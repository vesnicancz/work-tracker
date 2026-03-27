using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Interfaces;
using WorkTracker.Infrastructure.Data;
using WorkTracker.Infrastructure.Repositories;

namespace WorkTracker.Infrastructure.Tests;

public class DependencyInjectionTests : IAsyncDisposable
{
	private readonly ServiceCollection _services;
	private readonly IConfiguration _configuration;
	private ServiceProvider? _serviceProvider;
	private readonly string _testDbPath;

	public DependencyInjectionTests()
	{
		_services = new ServiceCollection();

		// Add logging for tests
		_services.AddLogging(builder =>
		{
			builder.AddDebug();
			builder.AddConsole();
		});

		// Create minimal configuration with temp file database for factory pattern
		_testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = _testDbPath
		};

		_configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();
	}

	public async ValueTask DisposeAsync()
	{
		if (_serviceProvider != null)
		{
			await _serviceProvider.DisposeAsync();
		}

		// Clean up test database
		if (File.Exists(_testDbPath))
		{
			try
			{
				File.Delete(_testDbPath);
			}
			catch
			{
				// Ignore cleanup errors
			}
		}
	}

	#region Service Registration Tests

	[Fact]
	public void AddInfrastructure_ShouldRegisterDbContextFactory()
	{
		// Act
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Assert
		var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<WorkTrackerDbContext>>();
		dbContextFactory.Should().NotBeNull();
	}

	[Fact]
	public async Task DbContextFactory_ShouldCreateWorkingDbContext()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var dbContext = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

		// Assert
		dbContext.Should().NotBeNull();
		dbContext.Should().BeOfType<WorkTrackerDbContext>();
	}

	[Fact]
	public void AddInfrastructure_ShouldRegisterWorkEntryRepository()
	{
		// Act
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Assert
		var repository = _serviceProvider.GetService<IWorkEntryRepository>();
		repository.Should().NotBeNull();
		repository.Should().BeOfType<WorkEntryRepository>();
	}

	[Fact]
	public void AddInfrastructure_ShouldRegisterDateRangeService()
	{
		// Act
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Assert
		var service = _serviceProvider.GetService<IDateRangeService>();
		service.Should().NotBeNull();
		service.Should().BeOfType<DateRangeService>();
	}

	[Fact]
	public void AddInfrastructure_ShouldRegisterWorklogValidator()
	{
		// Act
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Assert
		var validator = _serviceProvider.GetService<IWorklogValidator>();
		validator.Should().NotBeNull();
		validator.Should().BeOfType<WorklogValidator>();
	}

	[Fact]
	public void AddInfrastructure_ShouldRegisterPluginManager()
	{
		// Act
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Assert
		var pluginManager = _serviceProvider.GetService<PluginManager>();
		pluginManager.Should().NotBeNull();
	}

	[Fact]
	public void AddInfrastructure_ShouldRegisterWorklogSubmissionService()
	{
		// Act
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Assert
		var service = _serviceProvider.GetService<IWorklogSubmissionService>();
		service.Should().NotBeNull();
		service.Should().BeOfType<PluginBasedWorklogSubmissionService>();
	}

	#endregion Service Registration Tests

	#region Service Lifetime Tests

	[Fact]
	public async Task DbContextFactory_ShouldCreateNewDbContextEachTime()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var dbContext1 = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
		await using var dbContext2 = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

		// Assert - Each call should create a new instance
		dbContext1.Should().NotBeSameAs(dbContext2);
	}

	[Fact]
	public void Repository_ShouldBeTransient()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var repo1 = _serviceProvider.GetRequiredService<IWorkEntryRepository>();
		var repo2 = _serviceProvider.GetRequiredService<IWorkEntryRepository>();

		// Assert - Transient should return different instances
		repo1.Should().NotBeSameAs(repo2);
	}

	[Fact]
	public void PluginManager_ShouldBeSingleton()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var pluginManager1 = _serviceProvider.GetRequiredService<PluginManager>();
		var pluginManager2 = _serviceProvider.GetRequiredService<PluginManager>();

		// Assert
		pluginManager1.Should().BeSameAs(pluginManager2);
	}

	[Fact]
	public void DateRangeService_ShouldBeTransient()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var service1 = _serviceProvider.GetRequiredService<IDateRangeService>();
		var service2 = _serviceProvider.GetRequiredService<IDateRangeService>();

		// Assert - Transient should return different instances
		service1.Should().NotBeSameAs(service2);
	}

	[Fact]
	public void WorklogSubmissionService_ShouldBeTransient()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var service1 = _serviceProvider.GetRequiredService<IWorklogSubmissionService>();
		var service2 = _serviceProvider.GetRequiredService<IWorklogSubmissionService>();

		// Assert - Transient should return different instances
		service1.Should().NotBeSameAs(service2);
	}

	[Fact]
	public void WorkEntryService_ShouldBeTransient()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var service1 = _serviceProvider.GetRequiredService<IWorkEntryService>();
		var service2 = _serviceProvider.GetRequiredService<IWorkEntryService>();

		// Assert - Transient should return different instances
		service1.Should().NotBeSameAs(service2);
	}

	#endregion Service Lifetime Tests

	#region Configuration Tests

	[Fact]
	public async Task AddInfrastructure_WithCustomDatabasePath_ShouldUseIt()
	{
		// Arrange
		var customPath = Path.Combine(Path.GetTempPath(), "custom-test.db");
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = customPath
		};
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		_services.AddLogging();
		_services.AddInfrastructure(config);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var dbContext = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

		// Assert
		dbContext.Should().NotBeNull();
		// The connection string should contain the custom path
		dbContext.Database.GetConnectionString().Should().Contain(customPath);
	}

	[Fact]
	public async Task AddInfrastructure_WithoutDatabasePath_ShouldUseDefault()
	{
		// Arrange
		var configData = new Dictionary<string, string?>();
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		_services.AddLogging();
		_services.AddInfrastructure(config);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var dbContext = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);

		// Assert
		dbContext.Should().NotBeNull();
		dbContext.Database.GetConnectionString().Should().Contain("worktracker.db");
	}

	#endregion Configuration Tests

	#region Service Resolution Tests

	[Fact]
	public void AllRegisteredServices_ShouldBeResolvable()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act & Assert - All services should resolve without errors
		_serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<IWorkEntryRepository>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<IDateRangeService>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<IWorklogValidator>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<PluginManager>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<IPluginManager>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<IWorklogSubmissionService>().Should().NotBeNull();
		_serviceProvider.GetRequiredService<IWorkEntryService>().Should().NotBeNull();
	}

	[Fact]
	public void WorklogSubmissionService_ShouldHaveAllDependencies()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var service = _serviceProvider.GetRequiredService<IWorklogSubmissionService>();

		// Assert
		service.Should().NotBeNull();
		service.Should().BeOfType<PluginBasedWorklogSubmissionService>();
	}

	[Fact]
	public void Repository_ShouldHaveDbContextFactory()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var repository = _serviceProvider.GetRequiredService<IWorkEntryRepository>();

		// Assert
		repository.Should().NotBeNull();
		repository.Should().BeOfType<WorkEntryRepository>();
	}

	#endregion Service Resolution Tests

	#region InitializeDatabaseAsync Tests

	[Fact]
	public async Task InitializeDatabaseAsync_ShouldNotThrow()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var act = async () => await DependencyInjection.InitializeDatabaseAsync(_serviceProvider, TestContext.Current.CancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task InitializeDatabaseAsync_ShouldCreateDatabase()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		await DependencyInjection.InitializeDatabaseAsync(_serviceProvider, TestContext.Current.CancellationToken);

		// Assert
		var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<WorkTrackerDbContext>>();
		await using var dbContext = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
		var canConnect = await dbContext.Database.CanConnectAsync(TestContext.Current.CancellationToken);
		canConnect.Should().BeTrue();
	}

	#endregion InitializeDatabaseAsync Tests

	#region Plugin Directory Configuration Tests

	[Fact]
	public void AddInfrastructure_WithConfiguredPluginDirectories_ShouldResolvePluginManager()
	{
		// Arrange
		var tempDir = Path.Combine(Path.GetTempPath(), $"plugins_test_{Guid.NewGuid()}");
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = _testDbPath,
			["Plugins:Directories:0"] = tempDir
		};
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		try
		{
			// Act
			_services.AddInfrastructure(config);
			_serviceProvider = _services.BuildServiceProvider();
			var pluginManager = _serviceProvider.GetRequiredService<PluginManager>();

			// Assert
			pluginManager.Should().NotBeNull();
			Directory.Exists(tempDir).Should().BeTrue("configured plugin directory should be created");
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir, true);
			}
		}
	}

	[Fact]
	public void AddInfrastructure_WithEmptyPluginDirectories_ShouldFallbackToDefault()
	{
		// Arrange
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = _testDbPath,
			["Plugins:Directories:0"] = "  ",
			["Plugins:Directories:1"] = ""
		};
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		// Act
		_services.AddInfrastructure(config);
		_serviceProvider = _services.BuildServiceProvider();
		var pluginManager = _serviceProvider.GetRequiredService<PluginManager>();

		// Assert — should not throw, falls back to default
		pluginManager.Should().NotBeNull();
	}

	[Fact]
	public void AddInfrastructure_WithRelativePluginDirectory_ShouldResolveAgainstBaseDirectory()
	{
		// Arrange
		var relativeName = $"plugins_rel_{Guid.NewGuid()}";
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = _testDbPath,
			["Plugins:Directories:0"] = relativeName
		};
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		var expectedDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativeName));

		try
		{
			// Act
			_services.AddInfrastructure(config);
			_serviceProvider = _services.BuildServiceProvider();
			var pluginManager = _serviceProvider.GetRequiredService<PluginManager>();

			// Assert
			pluginManager.Should().NotBeNull();
			Directory.Exists(expectedDir).Should().BeTrue("relative plugin directory should be resolved and created");
		}
		finally
		{
			if (Directory.Exists(expectedDir))
			{
				Directory.Delete(expectedDir, true);
			}
		}
	}

	#endregion Plugin Directory Configuration Tests

	#region InitializePluginsAsync Tests

	[Fact]
	public async Task InitializePluginsAsync_WithNoPlugins_ShouldNotThrow()
	{
		// Arrange
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = ":memory:"
		};
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		_services.AddInfrastructure(config);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var act = async () => await DependencyInjection.InitializePluginsAsync(_serviceProvider, config, cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task InitializePluginsAsync_WithPluginConfiguration_ShouldInitialize()
	{
		// Arrange
		var configData = new Dictionary<string, string?>
		{
			["Database:Path"] = ":memory:",
			["Plugins:test.plugin:ApiKey"] = "test-key",
			["Plugins:test.plugin:BaseUrl"] = "https://test.com"
		};
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(configData)
			.Build();

		_services.AddInfrastructure(config);
		_serviceProvider = _services.BuildServiceProvider();

		// Act
		var act = async () => await DependencyInjection.InitializePluginsAsync(_serviceProvider, config, cancellationToken: TestContext.Current.CancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	#endregion InitializePluginsAsync Tests

	#region Factory Pattern Tests

	[Fact]
	public async Task Repository_ShouldCreateNewDbContextForEachOperation()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();
		await DependencyInjection.InitializeDatabaseAsync(_serviceProvider, TestContext.Current.CancellationToken);

		var repository = _serviceProvider.GetRequiredService<IWorkEntryRepository>();

		// Act - Multiple operations should each use a new DbContext
		var entry1 = await repository.GetActiveWorkEntryAsync(TestContext.Current.CancellationToken);
		var entry2 = await repository.GetActiveWorkEntryAsync(TestContext.Current.CancellationToken);

		// Assert - Should not throw (would throw if DbContext was disposed and reused)
		entry1.Should().BeNull(); // No active entry in empty database
		entry2.Should().BeNull();
	}

	[Fact]
	public async Task WorkEntryService_ShouldWorkWithTransientLifetime()
	{
		// Arrange
		_services.AddInfrastructure(_configuration);
		_serviceProvider = _services.BuildServiceProvider();
		await DependencyInjection.InitializeDatabaseAsync(_serviceProvider, TestContext.Current.CancellationToken);

		// Act - Each service instance is transient
		var service1 = _serviceProvider.GetRequiredService<IWorkEntryService>();
		var result1 = await service1.StartWorkAsync("TEST-1", description: "Test work", cancellationToken: TestContext.Current.CancellationToken);

		var service2 = _serviceProvider.GetRequiredService<IWorkEntryService>();
		var result2 = await service2.StopWorkAsync(cancellationToken: TestContext.Current.CancellationToken);

		var service3 = _serviceProvider.GetRequiredService<IWorkEntryService>();
		var result3 = await service3.GetWorkEntriesByDateAsync(DateTime.Today, TestContext.Current.CancellationToken);

		// Assert - All operations should succeed despite using different service instances
		result1.IsSuccess.Should().BeTrue();
		result2.IsSuccess.Should().BeTrue();
		result3.Should().NotBeNull();
		result3.Should().HaveCount(1);
	}

	#endregion Factory Pattern Tests
}