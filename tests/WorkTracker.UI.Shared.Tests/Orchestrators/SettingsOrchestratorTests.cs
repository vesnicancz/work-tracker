using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class SettingsOrchestratorTests
{
	private readonly Mock<ISettingsService> _mockSettingsService;
	private readonly Mock<IPluginManager> _mockPluginManager;
	private readonly Mock<IConfiguration> _mockConfiguration;
	private readonly Mock<IAutostartManager> _mockAutostartManager;
	private readonly Mock<ITrayIconService> _mockTrayIconService;
	private readonly SettingsOrchestrator _orchestrator;

	public SettingsOrchestratorTests()
	{
		_mockSettingsService = new Mock<ISettingsService>();
		_mockPluginManager = new Mock<IPluginManager>();
		_mockConfiguration = new Mock<IConfiguration>();
		_mockAutostartManager = new Mock<IAutostartManager>();
		_mockTrayIconService = new Mock<ITrayIconService>();

		_mockSettingsService.Setup(s => s.Settings).Returns(new ApplicationSettings());

		_orchestrator = new SettingsOrchestrator(
			_mockSettingsService.Object,
			_mockPluginManager.Object,
			_mockConfiguration.Object,
			_mockAutostartManager.Object,
			_mockTrayIconService.Object,
			new Mock<ILogger<SettingsOrchestrator>>().Object);
	}

	#region LoadPlugins

	[Fact]
	public void LoadPlugins_NoPlugins_ReturnsEmptyList()
	{
		_mockPluginManager.Setup(p => p.LoadedPlugins).Returns(new Dictionary<string, IPlugin>());

		var result = _orchestrator.LoadPlugins();

		result.Should().BeEmpty();
	}

	[Fact]
	public void LoadPlugins_WithPlugin_ReturnsPluginViewModels()
	{
		var plugin = CreateMockPlugin("test-plugin", "Test Plugin");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPlugin> { ["test-plugin"] = plugin.Object });

		var result = _orchestrator.LoadPlugins();

		result.Should().HaveCount(1);
		result[0].Name.Should().Be("Test Plugin");
	}

	[Fact]
	public void LoadPlugins_LoadsEnabledState()
	{
		var plugin = CreateMockPlugin("test-plugin", "Test");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPlugin> { ["test-plugin"] = plugin.Object });

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
		var worklogPlugin = CreateMockWorklogPlugin("tempo", "Tempo");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPlugin> { ["tempo"] = worklogPlugin.Object });

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
		var worklogPlugin = CreateMockWorklogPlugin("tempo", "Tempo");
		_mockPluginManager.Setup(p => p.LoadedPlugins)
			.Returns(new Dictionary<string, IPlugin> { ["tempo"] = worklogPlugin.Object });

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
		var pluginVm = new PluginViewModel(CreateMockPlugin("tempo", "Tempo").Object);
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

	#endregion SaveSettingsAsync

	#region TestConnectionAsync

	[Fact]
	public async Task TestConnectionAsync_NonWorklogPlugin_ReturnsNotAvailable()
	{
		var plugin = CreateMockPlugin("basic", "Basic");
		var vm = new PluginViewModel(plugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, TestContext.Current.CancellationToken);

		result.Should().Contain("not available");
	}

	[Fact]
	public async Task TestConnectionAsync_Success_ReturnsSuccessMessage()
	{
		var worklogPlugin = CreateMockWorklogPlugin("tempo", "Tempo");
		worklogPlugin.Setup(p => p.TestConnectionAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Success(true));

		var vm = new PluginViewModel(worklogPlugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, TestContext.Current.CancellationToken);

		result.Should().Contain("successful");
	}

	[Fact]
	public async Task TestConnectionAsync_Failure_ReturnsErrorMessage()
	{
		var worklogPlugin = CreateMockWorklogPlugin("tempo", "Tempo");
		worklogPlugin.Setup(p => p.TestConnectionAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<bool>.Failure("Auth failed"));

		var vm = new PluginViewModel(worklogPlugin.Object);

		var result = await _orchestrator.TestConnectionAsync(vm, TestContext.Current.CancellationToken);

		result.Should().Contain("Auth failed");
	}

	#endregion TestConnectionAsync

	private static Mock<IPlugin> CreateMockPlugin(string id, string name)
	{
		var plugin = new Mock<IPlugin>();
		plugin.Setup(p => p.Metadata).Returns(new PluginMetadata
		{
			Id = id,
			Name = name,
			Version = new Version(1, 0),
			Author = "Test"
		});
		return plugin;
	}

	private static Mock<IWorklogUploadPlugin> CreateMockWorklogPlugin(string id, string name)
	{
		var plugin = new Mock<IWorklogUploadPlugin>();
		plugin.Setup(p => p.Metadata).Returns(new PluginMetadata
		{
			Id = id,
			Name = name,
			Version = new Version(1, 0),
			Author = "Test"
		});
		plugin.Setup(p => p.GetConfigurationFields()).Returns(new List<PluginConfigurationField>
		{
			new() { Key = "ApiUrl", Label = "API URL", IsRequired = true }
		});
		plugin.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		return plugin;
	}
}