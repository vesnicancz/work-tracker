using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Infrastructure.Plugins;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Tests.Plugins;

public class PluginManagerTests : IAsyncDisposable
{
	private readonly Mock<ILoggerFactory> _mockLoggerFactory;
	private readonly PluginManager _pluginManager;

	public PluginManagerTests()
	{
		_mockLoggerFactory = new Mock<ILoggerFactory>();
		_mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
			.Returns(Mock.Of<ILogger>());
		_pluginManager = new PluginManager(_mockLoggerFactory.Object, Mock.Of<IHttpClientFactory>());
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
		_pluginManager.SetEnabledPlugins(["test.plugin"]);
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
		await _pluginManager.InitializePluginsAsync(null, TestContext.Current.CancellationToken);

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
		await _pluginManager.InitializePluginsAsync(null, TestContext.Current.CancellationToken);

		// Act
		await _pluginManager.DisposeAsync();

		// Assert
		_pluginManager.LoadedPlugins.Should().BeEmpty();
	}

	[Fact]
	public void LoadEmbeddedPlugin_WithPluginBaseDerived_ShouldInjectLogger()
	{
		// Act
		var result = _pluginManager.LoadEmbeddedPlugin<TestPluginBaseDerived>();

		// Assert — plugin gets ILogger<T> via DI, which calls CreateLogger with the full type name
		result.Should().BeTrue();
		_mockLoggerFactory.Verify(
			f => f.CreateLogger(typeof(TestPluginBaseDerived).FullName!),
			Times.Once);
	}

	#region SetEnabledPlugins filtering

	[Fact]
	public void SetEnabledPlugins_FiltersWorklogPlugins()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();

		_pluginManager.SetEnabledPlugins(["test.plugin"]);

		_pluginManager.WorklogUploadPlugins.Should().BeEmpty();
		_pluginManager.AllWorklogUploadPlugins.Should().HaveCount(1);
	}

	[Fact]
	public void SetEnabledPlugins_EmptyList_DisablesAll()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();

		_pluginManager.SetEnabledPlugins([]);

		_pluginManager.WorklogUploadPlugins.Should().BeEmpty();
	}

	[Fact]
	public void SetEnabledPlugins_UpdateReplacesOldSelection()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();
		_pluginManager.SetEnabledPlugins(["test.plugin", "test.worklog.plugin"]);

		_pluginManager.SetEnabledPlugins(["test.worklog.plugin"]);

		_pluginManager.WorklogUploadPlugins.Should().HaveCount(1);
		_pluginManager.GetPlugin("test.plugin").Should().NotBeNull();
	}

	#endregion

	#region InitializePluginsAsync — disabled skipping

	[Fact]
	public async Task InitializePluginsAsync_SkipsDisabledPlugins()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.SetEnabledPlugins([]);

		await _pluginManager.InitializePluginsAsync(null, TestContext.Current.CancellationToken);

		var plugin = _pluginManager.GetPlugin("test.plugin") as TestPlugin;
		plugin!.IsInitialized.Should().BeFalse();
	}

	#endregion

	#region DI propagation

	[Fact]
	public void LoadEmbeddedPlugin_PluginBasePlugin_ReceivesLogger()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPluginBaseDerived>();

		// Verify ILoggerFactory.CreateLogger was called for the plugin type
		_mockLoggerFactory.Verify(
			f => f.CreateLogger(typeof(TestPluginBaseDerived).FullName!),
			Times.Once);
	}

	[Fact]
	public void LoadEmbeddedPlugin_PluginWithHttpClientFactory_ReceivesFactory()
	{
		// TestPluginWithHttpClientFactory has IHttpClientFactory in constructor
		var result = _pluginManager.LoadEmbeddedPlugin<TestPluginWithHttpClientFactory>();

		result.Should().BeTrue();
		var plugin = _pluginManager.GetPlugin("test.httpclient.plugin") as TestPluginWithHttpClientFactory;
		plugin!.HasHttpClientFactory.Should().BeTrue();
	}

	[Fact]
	public void LoadEmbeddedPlugin_PluginWithTokenProviderFactory_ReceivesFactory()
	{
		var result = _pluginManager.LoadEmbeddedPlugin<TestPluginWithTokenProviderFactory>();

		result.Should().BeTrue();
		var plugin = _pluginManager.GetPlugin("test.tokenprovider.plugin") as TestPluginWithTokenProviderFactory;
		plugin!.HasTokenProviderFactory.Should().BeTrue();
	}

	#endregion

	#region Unload + Shutdown lifecycle

	[Fact]
	public async Task UnloadPluginAsync_CallsShutdownOnPlugin()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.SetEnabledPlugins(["test.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, TestContext.Current.CancellationToken);

		await _pluginManager.UnloadPluginAsync("test.plugin");

		// Plugin was removed — we can't access it anymore, but Shutdown was called
		_pluginManager.LoadedPlugins.Should().NotContainKey("test.plugin");
	}

	[Fact]
	public async Task DisposeAsync_UnloadsAllPlugins()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();

		await _pluginManager.DisposeAsync();

		_pluginManager.LoadedPlugins.Should().BeEmpty();
	}

	#endregion

	public async ValueTask DisposeAsync()
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

	public IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [];

	public Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginValidationResult.Success());
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

	public Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<bool>.Success(true));
	}

	public WorklogSubmissionMode SupportedModes => WorklogSubmissionMode.Timed;

	public Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<bool>.Success(true));
	}

	public Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, WorklogSubmissionMode mode, CancellationToken cancellationToken)
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

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class TestPluginWithHttpClientFactory(IHttpClientFactory httpClientFactory, ILogger<TestPluginWithHttpClientFactory> logger)
	: PluginBase(logger)
{
	public bool HasHttpClientFactory => httpClientFactory != null;

	public override PluginMetadata Metadata => new()
	{
		Id = "test.httpclient.plugin",
		Name = "Test HttpClient Plugin",
		Version = new Version(1, 0, 0),
		Author = "Test Author"
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [];
}

public class TestPluginWithTokenProviderFactory(ITokenProviderFactory tokenProviderFactory, ILogger<TestPluginWithTokenProviderFactory> logger)
	: PluginBase(logger)
{
	public bool HasTokenProviderFactory => tokenProviderFactory != null;

	public override PluginMetadata Metadata => new()
	{
		Id = "test.tokenprovider.plugin",
		Name = "Test TokenProvider Plugin",
		Version = new Version(1, 0, 0),
		Author = "Test Author"
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [];
}

public class TestPluginBaseDerived(ILogger<TestPluginBaseDerived> logger) : PluginBase(logger)
{
	public override PluginMetadata Metadata => new()
	{
		Id = "test.pluginbase",
		Name = "Test PluginBase Plugin",
		Version = new Version(1, 0, 0),
		Author = "Test Author"
	};

	public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [];

	protected override Task<bool> OnInitializeAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken)
		=> Task.FromResult(true);

	protected override Task OnShutdownAsync() => Task.CompletedTask;
}