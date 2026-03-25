using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Tests.Plugins;

public class PluginManagerTests : IDisposable
{
	private readonly Mock<ILogger<PluginManager>> _mockLogger;
	private readonly PluginManager _pluginManager;

	public PluginManagerTests()
	{
		_mockLogger = new Mock<ILogger<PluginManager>>();
		_pluginManager = new PluginManager(_mockLogger.Object);
	}

	[Fact]
	public void LoadEmbeddedPlugin_WithValidPlugin_ShouldSucceed()
	{
		// Act
		var result = _pluginManager.LoadEmbeddedPlugin<TestPlugin>();

		// Assert
		result.Should().BeTrue();
		_pluginManager.LoadedPlugins.Should().ContainKey("test.plugin");
		_pluginManager.LoadedPlugins["test.plugin"].Should().BeOfType<TestPlugin>();
	}

	[Fact]
	public void LoadEmbeddedPlugin_SamePluginTwice_ShouldFailSecondTime()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();

		// Act
		var result = _pluginManager.LoadEmbeddedPlugin<TestPlugin>();

		// Assert
		result.Should().BeFalse();
		_pluginManager.LoadedPlugins.Should().HaveCount(1);
	}

	[Fact]
	public void GetPlugin_WithExistingId_ShouldReturnPlugin()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();

		// Act
		var plugin = _pluginManager.GetPlugin("test.plugin");

		// Assert
		plugin.Should().NotBeNull();
		plugin.Should().BeOfType<TestPlugin>();
	}

	[Fact]
	public void GetPlugin_WithNonExistentId_ShouldReturnNull()
	{
		// Act
		var plugin = _pluginManager.GetPlugin("nonexistent");

		// Assert
		plugin.Should().BeNull();
	}

	[Fact]
	public void WorklogUploadPlugins_ShouldReturnOnlyWorklogPlugins()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();
		_pluginManager.SetEnabledPlugins(new[] { "test.plugin", "test.worklog.plugin" });

		// Act
		var worklogPlugins = _pluginManager.WorklogUploadPlugins.ToList();

		// Assert
		worklogPlugins.Should().HaveCount(1);
		worklogPlugins[0].Should().BeOfType<TestWorklogPlugin>();
	}

	[Fact]
	public async Task InitializePluginsAsync_ShouldInitializeAllPlugins()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		var config = new Dictionary<string, Dictionary<string, string>>
		{
			["test.plugin"] = new Dictionary<string, string>
			{
				["key1"] = "value1"
			}
		};

		// Act
		await _pluginManager.InitializePluginsAsync(config, TestContext.Current.CancellationToken);

		// Assert
		var plugin = _pluginManager.GetPlugin("test.plugin") as TestPlugin;
		plugin.Should().NotBeNull();
		plugin!.IsInitialized.Should().BeTrue();
	}

	[Fact]
	public async Task UnloadPluginAsync_WithExistingPlugin_ShouldSucceed()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		await _pluginManager.InitializePluginsAsync(cancellationToken: TestContext.Current.CancellationToken);

		// Act
		var result = await _pluginManager.UnloadPluginAsync("test.plugin");

		// Assert
		result.Should().BeTrue();
		_pluginManager.LoadedPlugins.Should().NotContainKey("test.plugin");
	}

	[Fact]
	public async Task UnloadPluginAsync_WithNonExistentPlugin_ShouldReturnFalse()
	{
		// Act
		var result = await _pluginManager.UnloadPluginAsync("nonexistent");

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void LoadedPlugins_ShouldBeReadOnly()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();

		// Act & Assert
		_pluginManager.LoadedPlugins.Should().BeAssignableTo<IReadOnlyDictionary<string, IPlugin>>();
	}

	[Fact]
	public async Task Dispose_ShouldUnloadAllPlugins()
	{
		// Arrange
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		await _pluginManager.InitializePluginsAsync(cancellationToken: TestContext.Current.CancellationToken);

		// Act
		await _pluginManager.DisposeAsync();

		// Assert
		_pluginManager.LoadedPlugins.Should().BeEmpty();
	}

	public async void Dispose()
	{
		if (_pluginManager != null)
		{
			await _pluginManager.DisposeAsync();
		}
	}
}

// Test plugins
public class TestPlugin : IPlugin
{
	public bool IsInitialized { get; private set; }
	public bool IsShutdown { get; private set; }

	public PluginMetadata Metadata => new()
	{
		Id = "test.plugin",
		Name = "Test Plugin",
		Version = new Version(1, 0, 0),
		Author = "Test Author",
		Description = "A test plugin"
	};

	public Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		IsInitialized = true;
		return Task.FromResult(true);
	}

	public Task ShutdownAsync()
	{
		IsShutdown = true;
		return Task.CompletedTask;
	}

	public Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginValidationResult.Success());
	}
}

public class TestWorklogPlugin : IWorklogUploadPlugin
{
	public bool IsInitialized { get; private set; }

	public PluginMetadata Metadata => new()
	{
		Id = "test.worklog.plugin",
		Name = "Test Worklog Plugin",
		Version = new Version(1, 0, 0),
		Author = "Test Author"
	};

	public Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		IsInitialized = true;
		return Task.FromResult(true);
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	public Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginValidationResult.Success());
	}

	public IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
	{
		return new List<PluginConfigurationField>();
	}

	public Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<bool>.Success(true));
	}

	public Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<bool>.Success(true));
	}

	public Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken)
	{
		var result = new WorklogSubmissionResult
		{
			TotalEntries = worklogs.Count(),
			SuccessfulEntries = worklogs.Count(),
			FailedEntries = 0
		};
		return Task.FromResult(PluginResult<WorklogSubmissionResult>.Success(result));
	}

	public Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<IEnumerable<PluginWorklogEntry>>.Success(Enumerable.Empty<PluginWorklogEntry>()));
	}

	public Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<bool>.Success(false));
	}
}