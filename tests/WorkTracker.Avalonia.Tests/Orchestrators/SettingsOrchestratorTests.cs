using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;
using WorkTracker.Tests.Common.Helpers;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Tests.Orchestrators;

public class SettingsOrchestratorTests
{
	private readonly Mock<ISettingsService> _mockSettingsService;
	private readonly Mock<IPluginManager> _mockPluginManager;
	private readonly Mock<ISecureStorage> _mockSecureStorage;
	private readonly Mock<IConfiguration> _mockConfiguration;
	private readonly Mock<IAutostartManager> _mockAutostartManager;
	private readonly Mock<ITrayIconService> _mockTrayIconService;
	private readonly Mock<IRemoteControlCoordinator> _mockRemoteControlCoordinator = new();
	private readonly Mock<IWorkSuggestionCache> _mockSuggestionCache = new();
	private readonly Mock<INotificationService> _mockNotificationService = new();
	private readonly Mock<ILocalizationService> _mockLocalization = new();
	private readonly SettingsOrchestrator _orchestrator;

	public SettingsOrchestratorTests()
	{
		_mockSettingsService = new Mock<ISettingsService>();
		_mockPluginManager = new Mock<IPluginManager>();
		_mockSecureStorage = new Mock<ISecureStorage>();
		_mockConfiguration = new Mock<IConfiguration>();
		_mockAutostartManager = new Mock<IAutostartManager>();
		_mockTrayIconService = new Mock<ITrayIconService>();

		_mockSettingsService.Setup(s => s.Settings).Returns(new ApplicationSettings());
		_mockSecureStorage.Setup(s => s.Protect(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns((string v, string _, string _) => v);
		_mockSecureStorage.Setup(s => s.Unprotect(It.IsAny<string>())).Returns((string v) => v);
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(It.IsAny<IPlugin>())).Returns((IPlugin p) => p);
		_mockPluginManager
			.Setup(m => m.InitializePluginsAsync(It.IsAny<Dictionary<string, Dictionary<string, string>>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);
		_mockLocalization
			.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] args) => $"{key}: {string.Join(", ", args)}");

		_orchestrator = new SettingsOrchestrator(
			_mockSettingsService.Object,
			_mockPluginManager.Object,
			_mockSecureStorage.Object,
			_mockConfiguration.Object,
			_mockAutostartManager.Object,
			_mockTrayIconService.Object,
			_mockRemoteControlCoordinator.Object,
			_mockSuggestionCache.Object,
			_mockNotificationService.Object,
			_mockLocalization.Object,
			new Mock<ILogger<SettingsOrchestrator>>().Object);
	}

	#region LoadPlugins

	[Fact]
	public void LoadPlugins_NoPlugins_ReturnsEmptyList()
	{
		_mockPluginManager.Setup(p => p.LoadedPlugins).Returns(new Dictionary<string, IPluginDescriptor>());

		var result = _orchestrator.LoadPlugins();

		result.Should().BeEmpty();
	}

	[Fact]
	public void LoadPlugins_WithPlugin_ReturnsPluginViewModels()
	{
		var plugin = MockPluginFactory.CreatePlugin("test-plugin", "Test Plugin");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPluginDescriptor> { ["test-plugin"] = plugin.Object });

		var result = _orchestrator.LoadPlugins();

		result.Should().HaveCount(1);
		result[0].Name.Should().Be("Test Plugin");
	}

	[Fact]
	public void LoadPlugins_LoadsEnabledState()
	{
		var plugin = MockPluginFactory.CreatePlugin("test-plugin", "Test");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPluginDescriptor> { ["test-plugin"] = plugin.Object });

		var settings = new ApplicationSettings
		{
			EnabledPlugins = new Dictionary<string, bool> { ["test-plugin"] = false }
		};
		_mockSettingsService.Setup(s => s.Settings).Returns(settings);

		var result = _orchestrator.LoadPlugins();

		result[0].IsEnabled.Should().BeFalse();
	}

	[Fact]
	public void LoadPlugins_LoadsSavedConfiguration()
	{
		var worklogPlugin = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo",
			new PluginConfigurationField { Key = "ApiUrl", Label = "API URL", IsRequired = true });
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPluginDescriptor> { ["tempo"] = worklogPlugin.Object });

		var settings = new ApplicationSettings
		{
			PluginConfigurations = new Dictionary<string, Dictionary<string, string>>
			{
				["tempo"] = new() { ["ApiUrl"] = "https://api.tempo.io" }
			}
		};
		_mockSettingsService.Setup(s => s.Settings).Returns(settings);

		var result = _orchestrator.LoadPlugins();

		result[0].Configuration.Should().ContainKey("ApiUrl");
		result[0].Configuration["ApiUrl"].Should().Be("https://api.tempo.io");
	}

	[Fact]
	public void LoadPlugins_FallsBackToAppSettings()
	{
		var worklogPlugin = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo",
			new PluginConfigurationField { Key = "ApiUrl", Label = "API URL", IsRequired = true });
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPluginDescriptor> { ["tempo"] = worklogPlugin.Object });

		var configSection = new Mock<IConfigurationSection>();
		configSection.Setup(s => s[It.IsAny<string>()]).Returns((string key) => key == "ApiUrl" ? "https://fallback.io" : null);
		_mockConfiguration.Setup(c => c.GetSection("Plugins:tempo")).Returns(configSection.Object);

		var result = _orchestrator.LoadPlugins();

		result[0].Configuration.Should().ContainKey("ApiUrl");
		result[0].Configuration["ApiUrl"].Should().Be("https://fallback.io");
	}

	#endregion LoadPlugins

	#region SaveSettingsAsync

	[Fact]
	public async Task SaveSettingsAsync_SavesAndAppliesAllSettings()
	{
		var pluginVm = new PluginViewModel(MockPluginFactory.CreatePlugin("tempo", "Tempo").Object);
		pluginVm.IsEnabled = true;
		pluginVm.Configuration["key"] = "value";

		var request = new SettingsSaveRequest
		{
			CloseWindowBehavior = CloseWindowBehavior.MinimizeToTray,
			StartWithWindows = true,
			StartMinimized = false,
			Theme = "Dark",
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel> { pluginVm }
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(It.Is<ApplicationSettings>(a =>
			a.CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray &&
			a.StartWithWindows == true &&
			a.Theme == "Dark"), It.IsAny<CancellationToken>()), Times.Once);
		_mockPluginManager.Verify(p => p.SetEnabledPlugins(It.IsAny<IEnumerable<string>>()), Times.Once);
		_mockPluginManager.Verify(p => p.InitializePluginsAsync(It.IsAny<Dictionary<string, Dictionary<string, string>>>(), It.IsAny<CancellationToken>()), Times.Once);
		_mockAutostartManager.Verify(a => a.SetAutostart(true), Times.Once);
		_mockTrayIconService.Verify(t => t.RefreshFavoritesMenu(), Times.Once);
		_mockNotificationService.Verify(n => n.ShowError(It.IsAny<string>()), Times.Never);
	}

	[Fact]
	public async Task SaveSettingsAsync_DropsCachedSuggestions()
	{
		await _orchestrator.SaveSettingsAsync(new SettingsSaveRequest
		{
			CloseWindowBehavior = CloseWindowBehavior.MinimizeToTray,
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		}, TestContext.Current.CancellationToken);

		_mockSuggestionCache.Verify(c => c.Invalidate(), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_PluginCouldNotStart_TellsTheUserAndFinishesSaving()
	{
		_mockPluginManager
			.Setup(m => m.InitializePluginsAsync(It.IsAny<Dictionary<string, Dictionary<string, string>>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([new PluginMetadata { Id = "tempo", Name = "Tempo", Version = new Version(1, 0), Author = "Test" }]);

		await _orchestrator.SaveSettingsAsync(new SettingsSaveRequest
		{
			CloseWindowBehavior = CloseWindowBehavior.MinimizeToTray,
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		}, TestContext.Current.CancellationToken);

		_mockNotificationService.Verify(n => n.ShowError(It.Is<string>(m => m.Contains("PluginInitializationFailed") && m.Contains("Tempo"))), Times.Once);
		_mockTrayIconService.Verify(t => t.RefreshFavoritesMenu(), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_SavesNotificationSettings()
	{
		_mockSettingsService.Setup(s => s.Settings).Returns(new ApplicationSettings());

		var request = new SettingsSaveRequest
		{
			ShowRemoteControlNotifications = false,
			Pomodoro = new PomodoroSettings { ShowSystemNotifications = false }
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(It.Is<ApplicationSettings>(a =>
			!a.ShowRemoteControlNotifications &&
			!a.Pomodoro.ShowSystemNotifications), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_NullTheme_PreservesExisting()
	{
		var existingSettings = new ApplicationSettings { Theme = "Midnight" };
		_mockSettingsService.Setup(s => s.Settings).Returns(existingSettings);

		var request = new SettingsSaveRequest
		{
			Theme = null,
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(
			It.Is<ApplicationSettings>(a => a.Theme == "Midnight"), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_PersistsFollowSystemThemeAndDayNightSelections()
	{
		var request = new SettingsSaveRequest
		{
			Theme = "Dark",
			FollowSystemTheme = true,
			LightTheme = "Cobalt",
			DarkTheme = "Eclipse",
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(It.Is<ApplicationSettings>(a =>
			a.FollowSystemTheme == true &&
			a.LightTheme == "Cobalt" &&
			a.DarkTheme == "Eclipse"), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_NullLightAndDarkTheme_PreservesExisting()
	{
		var existingSettings = new ApplicationSettings
		{
			LightTheme = "Sandstone",
			DarkTheme = "Synthwave"
		};
		_mockSettingsService.Setup(s => s.Settings).Returns(existingSettings);

		var request = new SettingsSaveRequest
		{
			LightTheme = null,
			DarkTheme = null,
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(It.Is<ApplicationSettings>(a =>
			a.LightTheme == "Sandstone" && a.DarkTheme == "Synthwave"),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_PersistsLanguage()
	{
		var request = new SettingsSaveRequest
		{
			Language = "cs",
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(
			It.Is<ApplicationSettings>(a => a.Language == "cs"), It.IsAny<CancellationToken>()), Times.Once);
	}

	/// <summary>
	/// SaveSettingsAsync rebuilds ApplicationSettings from scratch, so anything missing from the
	/// request is silently dropped. Guards the language against that.
	/// </summary>
	[Fact]
	public async Task SaveSettingsAsync_NullLanguage_PreservesExisting()
	{
		_mockSettingsService.Setup(s => s.Settings).Returns(new ApplicationSettings { Language = "en" });

		var request = new SettingsSaveRequest
		{
			Language = null,
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(
			It.Is<ApplicationSettings>(a => a.Language == "en"), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_UnknownLanguage_NormalizesToSystem()
	{
		var request = new SettingsSaveRequest
		{
			Language = "klingon",
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel>()
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSettingsService.Verify(s => s.SaveSettingsAsync(
			It.Is<ApplicationSettings>(a => a.Language == LanguageCatalog.SystemLanguage),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_ProtectsPasswordFields()
	{
		var plugin = CreateMockPluginWithPasswordField("tempo", "Tempo", "ApiToken");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPluginDescriptor> { ["tempo"] = plugin.Object });

		var pluginVm = new PluginViewModel(plugin.Object);
		pluginVm.IsEnabled = true;
		pluginVm.Configuration["ApiToken"] = "secret-value";

		_mockSecureStorage
			.Setup(s => s.Protect("secret-value", "tempo", "ApiToken"))
			.Returns("CS:tempo:ApiToken");

		var request = new SettingsSaveRequest
		{
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel> { pluginVm }
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSecureStorage.Verify(s => s.Protect("secret-value", "tempo", "ApiToken"), Times.Once);
		_mockSettingsService.Verify(s => s.SaveSettingsAsync(
			It.Is<ApplicationSettings>(a =>
				a.PluginConfigurations["tempo"]["ApiToken"] == "CS:tempo:ApiToken"),
			It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SaveSettingsAsync_RemovesCredentialWhenPasswordCleared()
	{
		var plugin = CreateMockPluginWithPasswordField("tempo", "Tempo", "ApiToken");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPluginDescriptor> { ["tempo"] = plugin.Object });

		var pluginVm = new PluginViewModel(plugin.Object);
		pluginVm.IsEnabled = true;
		// ApiToken not set — simulates cleared password

		var request = new SettingsSaveRequest
		{
			FavoriteWorkItems = new List<FavoriteWorkItem>(),
			Plugins = new List<PluginViewModel> { pluginVm }
		};

		await _orchestrator.SaveSettingsAsync(request, TestContext.Current.CancellationToken);

		_mockSecureStorage.Verify(s => s.Remove("tempo", "ApiToken"), Times.Once);
		_mockSecureStorage.Verify(s => s.Protect(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
	}

	#endregion SaveSettingsAsync

	#region TestConnectionAsync

	[Fact]
	public async Task TestConnectionAsync_NonWorklogPlugin_ReturnsNotAvailable()
	{
		var plugin = MockPluginFactory.CreatePlugin("basic", "Basic");
		var vm = new PluginViewModel(plugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, null, TestContext.Current.CancellationToken);

		result.Should().Contain("not available");
	}

	[Fact]
	public async Task TestConnectionAsync_Success_ReturnsSuccessMessage()
	{
		var worklogPlugin = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo",
			new PluginConfigurationField { Key = "ApiUrl", Label = "API URL", IsRequired = true });
		worklogPlugin.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Success(true));

		var vm = new PluginViewModel(worklogPlugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, null, TestContext.Current.CancellationToken);

		result.Should().Contain("successful");
	}

	[Fact]
	public async Task TestConnectionAsync_Failure_ReturnsErrorMessage()
	{
		var worklogPlugin = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo",
			new PluginConfigurationField { Key = "ApiUrl", Label = "API URL", IsRequired = true });
		worklogPlugin.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Failure("Auth failed"));

		var vm = new PluginViewModel(worklogPlugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, null, TestContext.Current.CancellationToken);

		result.Should().Contain("Auth failed");
	}

	[Fact]
	public async Task TestConnectionAsync_InvalidConfiguration_ReportsErrorsWithoutInitializing()
	{
		var worklogPlugin = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		worklogPlugin.Setup(p => p.ValidateConfigurationAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginValidationResult.Failure("Field 'API URL' must be an absolute http or https URL"));

		var vm = new PluginViewModel(worklogPlugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, null, TestContext.Current.CancellationToken);

		result.Should().StartWith("✗").And.Contain("Field 'API URL' must be an absolute http or https URL");
		worklogPlugin.Verify(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
		worklogPlugin.Verify(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task TestConnectionAsync_RunsOnASeparateInstanceAndLeavesTheRunningPluginAlone()
	{
		var running = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		var detached = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		detached.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
		detached.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Success(true));
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(running.Object)).Returns(detached.Object);
		var vm = new PluginViewModel(running.Object);
		vm.Configuration["ApiUrl"] = "https://unsaved";

		var result = await _orchestrator.TestConnectionAsync(vm, null, TestContext.Current.CancellationToken);

		result.Should().Contain("successful");
		detached.Verify(p => p.InitializeAsync(It.Is<IDictionary<string, string>>(c => c["ApiUrl"] == "https://unsaved"), It.IsAny<CancellationToken>()), Times.Once);
		detached.Verify(p => p.ShutdownAsync(), Times.Once);
		running.Verify(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
		running.Verify(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
		running.Verify(p => p.ShutdownAsync(), Times.Never);
	}

	[Fact]
	public async Task TestConnectionAsync_TestThrows_StillShutsTheSeparateInstanceDown()
	{
		var running = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		var detached = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		detached.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
		detached.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OperationCanceledException());
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(running.Object)).Returns(detached.Object);

		var act = () => _orchestrator.TestConnectionAsync(new PluginViewModel(running.Object), null, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<OperationCanceledException>();
		detached.Verify(p => p.ShutdownAsync(), Times.Once);
	}

	[Fact]
	public async Task TestConnectionAsync_InstanceCannotBeCreated_ReportsIt()
	{
		var running = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(running.Object)).Throws(new InvalidOperationException("no constructor"));

		var result = await _orchestrator.TestConnectionAsync(new PluginViewModel(running.Object), null, TestContext.Current.CancellationToken);

		result.Should().StartWith("✗").And.Contain("no constructor");
	}

	private static Mock<IStatusIndicatorPlugin> CreateStatusIndicator()
	{
		var plugin = new Mock<IStatusIndicatorPlugin>();
		plugin.Setup(p => p.Metadata).Returns(new PluginMetadata { Id = "luxafor", Name = "Luxafor", Version = new Version(1, 0), Author = "Test" });
		plugin.Setup(p => p.GetConfigurationFields()).Returns([]);
		plugin.Setup(p => p.ValidateConfigurationAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginValidationResult.Success());
		plugin.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
		plugin.As<ITestablePlugin>()
			.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Success(true));
		return plugin;
	}

	[Fact]
	public async Task TestConnectionAsync_StatusIndicator_PutsTheRunningOneAsideForTheTest()
	{
		var descriptor = CreateStatusIndicator();
		var detached = CreateStatusIndicator();
		var steps = new List<string>();
		var suspension = new Mock<IAsyncDisposable>();
		suspension.Setup(s => s.DisposeAsync()).Callback(() => steps.Add("resumed")).Returns(ValueTask.CompletedTask);
		_mockPluginManager
			.Setup(m => m.SuspendPluginAsync("luxafor", It.IsAny<CancellationToken>()))
			.Callback(() => steps.Add("suspended"))
			.ReturnsAsync(suspension.Object);
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(descriptor.Object)).Returns(detached.Object);
		detached.As<ITestablePlugin>()
			.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.Callback(() => steps.Add("tested"))
			.ReturnsAsync(PluginResult<bool>.Success(true));
		detached.Setup(p => p.ShutdownAsync()).Callback(() => steps.Add("test instance shut down")).Returns(Task.CompletedTask);
		var vm = new PluginViewModel(descriptor.Object);
		vm.Configuration["WorkColor"] = "Unsaved";

		var result = await _orchestrator.TestConnectionAsync(vm, null, TestContext.Current.CancellationToken);

		result.Should().Contain("successful");
		steps.Should().Equal("suspended", "tested", "test instance shut down", "resumed");
		detached.Verify(p => p.InitializeAsync(It.Is<IDictionary<string, string>>(c => c["WorkColor"] == "Unsaved"), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task TestConnectionAsync_StatusIndicatorTestThrows_StillResumesTheRunningOne()
	{
		var descriptor = CreateStatusIndicator();
		var detached = CreateStatusIndicator();
		var suspension = new Mock<IAsyncDisposable>();
		_mockPluginManager.Setup(m => m.SuspendPluginAsync("luxafor", It.IsAny<CancellationToken>())).ReturnsAsync(suspension.Object);
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(descriptor.Object)).Returns(detached.Object);
		detached.As<ITestablePlugin>()
			.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OperationCanceledException());

		var act = () => _orchestrator.TestConnectionAsync(new PluginViewModel(descriptor.Object), null, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<OperationCanceledException>();
		suspension.Verify(s => s.DisposeAsync(), Times.Once);
	}

	[Fact]
	public async Task TestConnectionAsync_OtherPlugin_DoesNotPutTheRunningOneAside()
	{
		var running = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		var detached = MockPluginFactory.CreateWorklogPlugin("tempo", "Tempo");
		detached.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
		detached.Setup(p => p.TestConnectionAsync(It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Success(true));
		_mockPluginManager.Setup(m => m.CreateDetachedInstance(running.Object)).Returns(detached.Object);

		await _orchestrator.TestConnectionAsync(new PluginViewModel(running.Object), null, TestContext.Current.CancellationToken);

		_mockPluginManager.Verify(m => m.SuspendPluginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	#endregion TestConnectionAsync

	#region ValidatePluginConfigurationsAsync

	[Fact]
	public async Task ValidatePluginConfigurationsAsync_AllValid_ReturnsEmpty()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin("tempo", "Tempo").Object) { IsEnabled = true };

		var errors = await _orchestrator.ValidatePluginConfigurationsAsync([vm], TestContext.Current.CancellationToken);

		errors.Should().BeEmpty();
	}

	[Fact]
	public async Task ValidatePluginConfigurationsAsync_EnabledPluginInvalid_PrefixesErrorsWithPluginName()
	{
		var plugin = MockPluginFactory.CreatePlugin("luxafor", "Luxafor");
		plugin.Setup(p => p.ValidateConfigurationAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginValidationResult.Failure("Bad colour", "Bad timeout"));
		var vm = new PluginViewModel(plugin.Object) { IsEnabled = true };

		var errors = await _orchestrator.ValidatePluginConfigurationsAsync([vm], TestContext.Current.CancellationToken);

		errors.Should().Equal("Luxafor: Bad colour", "Luxafor: Bad timeout");
	}

	[Fact]
	public async Task ValidatePluginConfigurationsAsync_DisabledPlugin_IsNotValidated()
	{
		var plugin = MockPluginFactory.CreatePlugin("luxafor", "Luxafor");
		plugin.Setup(p => p.ValidateConfigurationAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginValidationResult.Failure("Bad colour"));
		var vm = new PluginViewModel(plugin.Object) { IsEnabled = false };

		var errors = await _orchestrator.ValidatePluginConfigurationsAsync([vm], TestContext.Current.CancellationToken);

		errors.Should().BeEmpty();
		plugin.Verify(p => p.ValidateConfigurationAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	#endregion ValidatePluginConfigurationsAsync

	private static Mock<IPlugin> CreateMockPluginWithPasswordField(string id, string name, string passwordFieldKey)
	{
		var plugin = MockPluginFactory.CreatePlugin(id, name,
			new PluginConfigurationField { Key = passwordFieldKey, Label = "API Token", IsRequired = true, Type = PluginConfigurationFieldType.Password });
		plugin.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		return plugin;
	}
}
