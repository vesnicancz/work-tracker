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
		LifecycleTestPlugin.Reset();
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
	public void CreateDetachedInstance_ReturnsANewInstanceTheManagerDoesNotKnow()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		var running = _pluginManager.LoadedPlugins["test.plugin"];

		var detached = _pluginManager.CreateDetachedInstance(running);

		detached.Should().BeOfType<TestPlugin>().And.NotBeSameAs(running);
		_pluginManager.LoadedPlugins["test.plugin"].Should().BeSameAs(running);
		_pluginManager.LoadedPlugins.Should().ContainSingle();
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
	public async Task GetPlugin_ReturnsTheRunningInstance()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.SetEnabledPlugins(["test.plugin"]);

		_pluginManager.GetPlugin("test.plugin").Should().BeNull();

		await _pluginManager.InitializePluginsAsync(null, Ct);

		var plugin = _pluginManager.GetPlugin("test.plugin");
		plugin.Should().BeOfType<TestPlugin>().Which.IsInitialized.Should().BeTrue();
		plugin.Should().NotBeSameAs(_pluginManager.LoadedPlugins["test.plugin"]);
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
	public async Task WorklogUploadPlugins_ShouldReturnOnlyRunningWorklogPlugins()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();
		_pluginManager.SetEnabledPlugins(["test.plugin", "test.worklog.plugin"]);

		_pluginManager.WorklogUploadPlugins.Should().BeEmpty();
		_pluginManager.EnabledWorklogUploadPlugins.Should().ContainSingle().Which.Should().BeOfType<TestWorklogPlugin>();

		await _pluginManager.InitializePluginsAsync(null, Ct);

		_pluginManager.WorklogUploadPlugins.Should().ContainSingle().Which.Should().BeOfType<TestWorklogPlugin>();
	}

	private static Dictionary<string, Dictionary<string, string>> ConfigWith(string pluginId, string value) => new()
	{
		[pluginId] = new Dictionary<string, string> { ["key"] = value }
	};

	[Fact]
	public async Task InitializePluginsAsync_SameConfigurationAgain_LeavesThePluginAlone()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);

		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);
		var running = _pluginManager.GetPlugin("counting.plugin");
		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);

		_pluginManager.GetPlugin("counting.plugin").Should().BeSameAs(running);
		LifecycleTestPlugin.Initialized("counting.plugin").Should().Be(1);
	}

	[Fact]
	public async Task InitializePluginsAsync_PluginNotPicked_StaysListedWithoutStarting()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);

		var failed = await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), _ => false, Ct);

		failed.Should().BeEmpty();
		_pluginManager.LoadedPlugins.Should().ContainKey("counting.plugin");
		_pluginManager.GetPlugin("counting.plugin").Should().BeNull();
		LifecycleTestPlugin.Initialized("counting.plugin").Should().Be(0);
	}

	[Fact]
	public async Task InitializePluginsAsync_ChangedConfiguration_ReplacesTheInstance()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);

		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);
		var first = _pluginManager.GetPlugin("counting.plugin");
		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "b"), Ct);
		var second = _pluginManager.GetPlugin("counting.plugin");
		await _pluginManager.InitializePluginsAsync(null, Ct);
		var third = _pluginManager.GetPlugin("counting.plugin");

		second.Should().NotBeSameAs(first);
		third.Should().NotBeSameAs(second);
		((CountingPlugin)first!).ShutdownCount.Should().Be(1);
		((CountingPlugin)second!).ShutdownCount.Should().Be(1);
		((CountingPlugin)third!).ShutdownCount.Should().Be(0);
	}

	[Fact]
	public async Task InitializePluginsAsync_Disabled_ShutsTheInstanceDown()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, Ct);
		var running = (CountingPlugin)_pluginManager.GetPlugin("counting.plugin")!;

		_pluginManager.SetEnabledPlugins([]);
		await _pluginManager.InitializePluginsAsync(null, Ct);

		running.ShutdownCount.Should().Be(1);
		_pluginManager.GetPlugin("counting.plugin").Should().BeNull();
	}

	[Fact]
	public async Task InitializePluginsAsync_EnabledAgain_StartsANewInstance()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, Ct);
		var first = _pluginManager.GetPlugin("counting.plugin");

		_pluginManager.SetEnabledPlugins([]);
		await _pluginManager.InitializePluginsAsync(null, Ct);
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, Ct);

		_pluginManager.GetPlugin("counting.plugin").Should().NotBeNull().And.NotBeSameAs(first);
	}

	[Fact]
	public async Task InitializePluginsAsync_NewConfigurationFails_KeepsTheOldInstanceRunning()
	{
		_pluginManager.LoadEmbeddedPlugin<PickyPlugin>();
		_pluginManager.SetEnabledPlugins(["picky.plugin"]);
		await _pluginManager.InitializePluginsAsync(ConfigWith("picky.plugin", "good"), Ct);
		var running = (PickyPlugin)_pluginManager.GetPlugin("picky.plugin")!;

		var failed = await _pluginManager.InitializePluginsAsync(ConfigWith("picky.plugin", "bad"), Ct);

		failed.Should().ContainSingle().Which.Id.Should().Be("picky.plugin");
		_pluginManager.GetPlugin("picky.plugin").Should().BeSameAs(running);
		running.ShutdownCount.Should().Be(0);
		LifecycleTestPlugin.ShutDown("picky.plugin").Should().Be(1, "the instance that failed to start gets shut down");
	}

	[Fact]
	public async Task InitializePluginsAsync_PluginReturnsFalse_IsReportedAndTriedAgainNextTime()
	{
		_pluginManager.LoadEmbeddedPlugin<RefusingPlugin>();
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["refusing.plugin", "counting.plugin"]);

		var first = await _pluginManager.InitializePluginsAsync(null, Ct);
		var second = await _pluginManager.InitializePluginsAsync(null, Ct);

		first.Should().ContainSingle().Which.Id.Should().Be("refusing.plugin");
		second.Should().ContainSingle();
		LifecycleTestPlugin.Initialized("refusing.plugin").Should().Be(2);
		LifecycleTestPlugin.Initialized("counting.plugin").Should().Be(1);
		_pluginManager.GetPlugin("refusing.plugin").Should().BeNull();
	}

	[Fact]
	public async Task InitializePluginsAsync_PluginThrows_IsReportedAndShutDown()
	{
		_pluginManager.LoadEmbeddedPlugin<ThrowingPlugin>();
		_pluginManager.SetEnabledPlugins(["throwing.plugin"]);

		var failed = await _pluginManager.InitializePluginsAsync(null, Ct);

		failed.Should().ContainSingle().Which.Id.Should().Be("throwing.plugin");
		LifecycleTestPlugin.ShutDown("throwing.plugin").Should().Be(1);
		_pluginManager.GetPlugin("throwing.plugin").Should().BeNull();
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
	public async Task InitializePluginsAsync_RunningAtTheSameTime_StartsThePluginOnce()
	{
		// The settings dialog can be saved while the plugins are still starting up
		_pluginManager.LoadEmbeddedPlugin<SlowPlugin>();
		_pluginManager.SetEnabledPlugins(["slow.plugin"]);

		await Task.WhenAll(
			_pluginManager.InitializePluginsAsync(ConfigWith("slow.plugin", "a"), Ct),
			_pluginManager.InitializePluginsAsync(ConfigWith("slow.plugin", "a"), Ct));

		LifecycleTestPlugin.Initialized("slow.plugin").Should().Be(1);
	}

	[Fact]
	public async Task InitializePluginsAsync_StatusIndicator_ShutsTheOldInstanceDownBeforeStartingTheNew()
	{
		// The device can only be held by one of them
		_pluginManager.LoadEmbeddedPlugin<TestStatusIndicatorPlugin>();
		_pluginManager.SetEnabledPlugins(["test.indicator"]);
		await _pluginManager.InitializePluginsAsync(ConfigWith("test.indicator", "a"), Ct);
		TestStatusIndicatorPlugin.Steps.Clear();

		await _pluginManager.InitializePluginsAsync(ConfigWith("test.indicator", "b"), Ct);

		TestStatusIndicatorPlugin.Steps.Should().Equal("shutdown", "initialize");
	}

	[Fact]
	public async Task PluginStarted_IsRaisedForEachNewInstance()
	{
		var started = new List<IPlugin>();
		_pluginManager.PluginStarted += (_, plugin) => started.Add(plugin);
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);

		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);
		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);
		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "b"), Ct);

		started.Should().HaveCount(2);
		started[1].Should().BeSameAs(_pluginManager.GetPlugin("counting.plugin"));
	}

	[Fact]
	public async Task PluginStarted_FailingSubscriber_DoesNotFailTheStart()
	{
		_pluginManager.PluginStarted += (_, _) => throw new InvalidOperationException("broken");
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);

		var failed = await _pluginManager.InitializePluginsAsync(null, Ct);

		failed.Should().BeEmpty();
	}

	[Fact]
	public async Task SuspendPluginAsync_ShutsDownAndResumesOnTheSameConfiguration()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);
		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);
		var running = (CountingPlugin)_pluginManager.GetPlugin("counting.plugin")!;

		var suspension = await _pluginManager.SuspendPluginAsync("counting.plugin", Ct);

		running.ShutdownCount.Should().Be(1);
		_pluginManager.GetPlugin("counting.plugin").Should().BeNull();

		await suspension.DisposeAsync();
		await suspension.DisposeAsync();

		var resumed = (CountingPlugin)_pluginManager.GetPlugin("counting.plugin")!;
		resumed.Should().NotBeSameAs(running);
		resumed.Configuration.Should().Contain("key", "a");
		LifecycleTestPlugin.Initialized("counting.plugin").Should().Be(2);
	}

	[Fact]
	public async Task SuspendPluginAsync_WhenNotRunning_DoesNotStartItAfterwards()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);

		await (await _pluginManager.SuspendPluginAsync("counting.plugin", Ct)).DisposeAsync();

		_pluginManager.GetPlugin("counting.plugin").Should().BeNull();
	}

	[Fact]
	public async Task SuspendPluginAsync_DisabledInTheMeantime_DoesNotResume()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, Ct);
		var suspension = await _pluginManager.SuspendPluginAsync("counting.plugin", Ct);

		_pluginManager.SetEnabledPlugins([]);
		await suspension.DisposeAsync();

		LifecycleTestPlugin.Initialized("counting.plugin").Should().Be(1);
	}

	[Fact]
	public async Task SuspendPluginAsync_SavedInTheMeantime_KeepsWhatTheSaveStarted()
	{
		_pluginManager.LoadEmbeddedPlugin<CountingPlugin>();
		_pluginManager.SetEnabledPlugins(["counting.plugin"]);
		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "a"), Ct);
		var suspension = await _pluginManager.SuspendPluginAsync("counting.plugin", Ct);

		await _pluginManager.InitializePluginsAsync(ConfigWith("counting.plugin", "b"), Ct);
		var started = _pluginManager.GetPlugin("counting.plugin");
		await suspension.DisposeAsync();

		_pluginManager.GetPlugin("counting.plugin").Should().BeSameAs(started);
		((CountingPlugin)started!).Configuration.Should().Contain("key", "b");
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
		_pluginManager.LoadedPlugins.Should().BeAssignableTo<IReadOnlyDictionary<string, IPluginDescriptor>>();
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

		_pluginManager.EnabledWorklogUploadPlugins.Should().BeEmpty();
		_pluginManager.AllWorklogUploadPlugins.Should().HaveCount(1);
	}

	[Fact]
	public void SetEnabledPlugins_EmptyList_DisablesAll()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();

		_pluginManager.SetEnabledPlugins([]);

		_pluginManager.EnabledWorklogUploadPlugins.Should().BeEmpty();
	}

	[Fact]
	public void SetEnabledPlugins_UpdateReplacesOldSelection()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();
		_pluginManager.SetEnabledPlugins(["test.plugin", "test.worklog.plugin"]);

		_pluginManager.SetEnabledPlugins(["test.worklog.plugin"]);

		_pluginManager.EnabledWorklogUploadPlugins.Should().HaveCount(1);
		_pluginManager.LoadedPlugins.Should().ContainKey("test.plugin");
	}

	[Fact]
	public async Task SetEnabledPlugins_RunningPluginNoLongerEnabled_IsNoLongerOffered()
	{
		_pluginManager.LoadEmbeddedPlugin<TestWorklogPlugin>();
		_pluginManager.SetEnabledPlugins(["test.worklog.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, Ct);

		_pluginManager.SetEnabledPlugins([]);

		_pluginManager.WorklogUploadPlugins.Should().BeEmpty();
		_pluginManager.GetPlugin("test.worklog.plugin").Should().BeNull();
	}

	#endregion

	#region InitializePluginsAsync — disabled skipping

	[Fact]
	public async Task InitializePluginsAsync_SkipsDisabledPlugins()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.SetEnabledPlugins([]);

		await _pluginManager.InitializePluginsAsync(null, TestContext.Current.CancellationToken);

		_pluginManager.GetPlugin("test.plugin").Should().BeNull();
		((TestPlugin)_pluginManager.LoadedPlugins["test.plugin"]).IsInitialized.Should().BeFalse();
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
		var plugin = (TestPluginWithHttpClientFactory)_pluginManager.LoadedPlugins["test.httpclient.plugin"];
		plugin.HasHttpClientFactory.Should().BeTrue();
	}

	[Fact]
	public void LoadEmbeddedPlugin_PluginWithTokenProviderFactory_ReceivesFactory()
	{
		var result = _pluginManager.LoadEmbeddedPlugin<TestPluginWithTokenProviderFactory>();

		result.Should().BeTrue();
		var plugin = (TestPluginWithTokenProviderFactory)_pluginManager.LoadedPlugins["test.tokenprovider.plugin"];
		plugin.HasTokenProviderFactory.Should().BeTrue();
	}

	#endregion

	#region Unload + Shutdown lifecycle

	[Fact]
	public async Task UnloadPluginAsync_CallsShutdownOnPlugin()
	{
		_pluginManager.LoadEmbeddedPlugin<TestPlugin>();
		_pluginManager.SetEnabledPlugins(["test.plugin"]);
		await _pluginManager.InitializePluginsAsync(null, TestContext.Current.CancellationToken);

		var running = (TestPlugin)_pluginManager.GetPlugin("test.plugin")!;

		await _pluginManager.UnloadPluginAsync("test.plugin");

		running.IsShutdown.Should().BeTrue();
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
public abstract class LifecycleTestPlugin(string id) : IPlugin
{
	private static readonly Dictionary<string, int> s_initialized = new();
	private static readonly Dictionary<string, int> s_shutDown = new();

	public static void Reset()
	{
		s_initialized.Clear();
		s_shutDown.Clear();
	}

	public static int Initialized(string pluginId) => s_initialized.GetValueOrDefault(pluginId);

	public static int ShutDown(string pluginId) => s_shutDown.GetValueOrDefault(pluginId);

	public int ShutdownCount { get; private set; }

	public IDictionary<string, string> Configuration { get; private set; } = new Dictionary<string, string>();

	public PluginMetadata Metadata => new() { Id = id, Name = id, Version = new Version(1, 0, 0), Author = "Test" };

	protected abstract Task<bool> InitializeCoreAsync();

	public Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		s_initialized[id] = Initialized(id) + 1;
		Configuration = configuration ?? new Dictionary<string, string>();
		return InitializeCoreAsync();
	}

	public virtual Task ShutdownAsync()
	{
		ShutdownCount++;
		s_shutDown[id] = ShutDown(id) + 1;
		return Task.CompletedTask;
	}

	public IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [];

	public Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken = default) =>
		Task.FromResult(PluginValidationResult.Success());

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class CountingPlugin() : LifecycleTestPlugin("counting.plugin")
{
	protected override Task<bool> InitializeCoreAsync() => Task.FromResult(true);
}

public sealed class RefusingPlugin() : LifecycleTestPlugin("refusing.plugin")
{
	protected override Task<bool> InitializeCoreAsync() => Task.FromResult(false);
}

public sealed class ThrowingPlugin() : LifecycleTestPlugin("throwing.plugin")
{
	protected override Task<bool> InitializeCoreAsync() => throw new InvalidOperationException("broken");
}

public sealed class PickyPlugin() : LifecycleTestPlugin("picky.plugin")
{
	protected override Task<bool> InitializeCoreAsync() => Task.FromResult(Configuration["key"] == "good");
}

public sealed class SlowPlugin() : LifecycleTestPlugin("slow.plugin")
{
	protected override async Task<bool> InitializeCoreAsync()
	{
		await Task.Delay(50);
		return true;
	}
}

public sealed class TestStatusIndicatorPlugin : IStatusIndicatorPlugin
{
	public static List<string> Steps { get; } = [];

	public PluginMetadata Metadata => new() { Id = "test.indicator", Name = "Test Indicator", Version = new Version(1, 0, 0), Author = "Test" };

	public bool IsDeviceAvailable => true;

	public Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken) => Task.CompletedTask;

	public Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default)
	{
		Steps.Add("initialize");
		return Task.FromResult(true);
	}

	public Task ShutdownAsync()
	{
		Steps.Add("shutdown");
		return Task.CompletedTask;
	}

	public IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [];

	public Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken = default) =>
		Task.FromResult(PluginValidationResult.Success());

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

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