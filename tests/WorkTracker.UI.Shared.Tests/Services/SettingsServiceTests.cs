using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Services;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

public class SettingsServiceTests : IDisposable
{
	private readonly Mock<ILogger<SettingsService>> _mockLogger = new();
	private readonly Mock<ISecureStorage> _mockSecureStorage = new();
	private readonly string _settingsDir;

	public SettingsServiceTests()
	{
		_settingsDir = Path.Combine(Path.GetTempPath(), $"WorkTracker_Test_{Guid.NewGuid():N}");

		// Default: pass-through for Unprotect
		_mockSecureStorage.Setup(s => s.Unprotect(It.IsAny<string>())).Returns((string v) => v);
	}

	public void Dispose()
	{
		if (Directory.Exists(_settingsDir))
		{
			Directory.Delete(_settingsDir, recursive: true);
		}
	}

	private SettingsService CreateSut() =>
		new(_mockLogger.Object, _mockSecureStorage.Object, _settingsDir);

	private void WriteSettingsFile(ApplicationSettings settings)
	{
		Directory.CreateDirectory(_settingsDir);
		var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), json);
	}

	private void WriteRawSettingsFile(string content)
	{
		Directory.CreateDirectory(_settingsDir);
		File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), content);
	}

	#region Constructor / LoadSettings

	[Fact]
	public void Constructor_NoSettingsFile_ReturnsDefaults()
	{
		var sut = CreateSut();

		sut.Settings.Should().NotBeNull();
		sut.Settings.Theme.Should().Be(ApplicationSettings.DefaultTheme);
		sut.Settings.StartMinimized.Should().BeFalse();
		sut.Settings.StartWithWindows.Should().BeFalse();
	}

	[Fact]
	public void Constructor_ValidSettingsFile_LoadsCorrectly()
	{
		var settings = new ApplicationSettings
		{
			Theme = "Dark",
			StartMinimized = true,
			StartWithWindows = true
		};
		WriteSettingsFile(settings);

		var sut = CreateSut();

		sut.Settings.Theme.Should().Be("Dark");
		sut.Settings.StartMinimized.Should().BeTrue();
		sut.Settings.StartWithWindows.Should().BeTrue();
	}

	[Theory]
	[InlineData("{{{invalid json!@#$", "corrupted")]
	[InlineData("", "empty")]
	[InlineData("null", "null")]
	public void Constructor_InvalidSettingsFile_ReturnsDefaults(string content, string _)
	{
		WriteRawSettingsFile(content);

		var sut = CreateSut();

		sut.Settings.Should().NotBeNull();
		sut.Settings.Theme.Should().Be(ApplicationSettings.DefaultTheme);
	}

	#endregion

	#region SaveSettings + LoadSettings round-trip

	[Fact]
	public void SaveThenLoad_PreservesValues()
	{
		var sut = CreateSut();

		var settings = new ApplicationSettings
		{
			Theme = "Custom Theme",
			StartMinimized = true,
			StartWithWindows = true
		};

		sut.SaveSettings(settings);

		// Create a new instance to reload from file
		var sut2 = CreateSut();
		sut2.Settings.Theme.Should().Be("Custom Theme");
		sut2.Settings.StartMinimized.Should().BeTrue();
	}

	[Fact]
	public void SaveSettings_WithPluginConfigs_WritesJson()
	{
		var sut = CreateSut();

		var settings = new ApplicationSettings
		{
			PluginConfigurations = new Dictionary<string, Dictionary<string, string>>
			{
				["tempo"] = new() { ["ApiUrl"] = "https://api.tempo.io" }
			}
		};

		sut.SaveSettings(settings);

		var json = File.ReadAllText(Path.Combine(_settingsDir, "settings.json"));
		json.Should().Contain("tempo");
		json.Should().Contain("https://api.tempo.io");
	}

	#endregion

	#region Async operations

	[Fact]
	public async Task SaveAsyncThenLoadAsync_RoundTrip()
	{
		var sut = CreateSut();

		var settings = new ApplicationSettings { Theme = "Async Theme" };

		await sut.SaveSettingsAsync(settings, TestContext.Current.CancellationToken);

		var loaded = await sut.LoadSettingsAsync(TestContext.Current.CancellationToken);
		loaded.Theme.Should().Be("Async Theme");
	}

	#endregion

	#region Settings property caching

	[Fact]
	public void Settings_ReturnsCachedInstance()
	{
		var sut = CreateSut();

		var s1 = sut.Settings;
		var s2 = sut.Settings;
		s1.Should().BeSameAs(s2);
	}

	[Fact]
	public void SaveSettings_UpdatesCache()
	{
		var sut = CreateSut();

		var newSettings = new ApplicationSettings { Theme = "Updated" };
		sut.SaveSettings(newSettings);

		sut.Settings.Theme.Should().Be("Updated");
	}

	#endregion

	#region Secure storage integration

	[Fact]
	public void LoadSettings_WithProtectedValues_CallsUnprotect()
	{
		var settings = new ApplicationSettings
		{
			PluginConfigurations = new Dictionary<string, Dictionary<string, string>>
			{
				["tempo"] = new()
				{
					["ApiToken"] = "CS:tempo:ApiToken",
					["ApiUrl"] = "https://api.tempo.io"
				}
			}
		};
		WriteSettingsFile(settings);

		_mockSecureStorage.Setup(s => s.Unprotect("CS:tempo:ApiToken")).Returns("decrypted-token");
		_mockSecureStorage.Setup(s => s.Unprotect("https://api.tempo.io")).Returns("https://api.tempo.io");

		var sut = CreateSut();

		sut.Settings.PluginConfigurations["tempo"]["ApiToken"].Should().Be("decrypted-token");
		sut.Settings.PluginConfigurations["tempo"]["ApiUrl"].Should().Be("https://api.tempo.io");
	}

	[Fact]
	public void LoadSettings_NullPluginConfigurations_DoesNotThrow()
	{
		// Settings with null PluginConfigurations (not serialized)
		WriteRawSettingsFile("""{"Theme": "Dark"}""");

		var act = () => CreateSut();
		act.Should().NotThrow();
	}

	#endregion

	#region Language

	[Fact]
	public void Constructor_NoSettingsFile_DefaultsToSystemLanguage()
	{
		var sut = CreateSut();

		sut.Settings.Language.Should().Be(LanguageCatalog.SystemLanguage);
	}

	[Fact]
	public void SaveSettings_Language_RoundTrips()
	{
		var sut = CreateSut();

		sut.SaveSettings(new ApplicationSettings { Language = "cs" });

		CreateSut().Settings.Language.Should().Be("cs");
	}

	/// <summary>
	/// Language is a string, not an enum, precisely so an unrecognised value cannot throw during
	/// deserialization - which would send LoadSettings into its catch and discard the whole file.
	/// </summary>
	[Fact]
	public void Constructor_UnknownLanguage_KeepsRemainingSettings()
	{
		WriteRawSettingsFile("""{"Theme": "Dark", "Language": "klingon"}""");

		var sut = CreateSut();

		sut.Settings.Theme.Should().Be("Dark");
	}

	#endregion

	#region Plugin state preservation

	private static ApplicationSettings SettingsWithPlugins(params (string Id, string Token, bool Enabled)[] plugins)
	{
		var settings = new ApplicationSettings();
		foreach (var (id, token, enabled) in plugins)
		{
			settings.PluginConfigurations[id] = new Dictionary<string, string> { ["ApiToken"] = token };
			settings.EnabledPlugins[id] = enabled;
		}

		return settings;
	}

	/// <summary>
	/// The regression that cost a real configuration: a development build with an empty plugins
	/// directory loaded the settings, saved them back with no plugins in them, and wiped every
	/// plugin's configuration and enabled state.
	/// </summary>
	[Fact]
	public async Task SaveSettingsAsync_NoPluginsInRequest_KeepsStoredPluginState()
	{
		WriteSettingsFile(SettingsWithPlugins(("tempo.worklog", "CS:tempo.worklog:ApiToken", true)));
		var sut = CreateSut();

		var saved = sut.Settings.Clone();
		saved.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>();
		saved.EnabledPlugins = new Dictionary<string, bool>();

		await sut.SaveSettingsAsync(saved, TestContext.Current.CancellationToken);

		var onDisk = JsonSerializer.Deserialize<ApplicationSettings>(
			await File.ReadAllTextAsync(Path.Combine(_settingsDir, "settings.json"), TestContext.Current.CancellationToken))!;
		onDisk.PluginConfigurations.Should().ContainKey("tempo.worklog");
		onDisk.PluginConfigurations["tempo.worklog"]["ApiToken"].Should().Be("CS:tempo.worklog:ApiToken");
		onDisk.EnabledPlugins["tempo.worklog"].Should().BeTrue();
	}

	[Fact]
	public async Task SaveSettingsAsync_PluginInRequest_OverwritesStoredState()
	{
		WriteSettingsFile(SettingsWithPlugins(
			("tempo.worklog", "CS:tempo.worklog:ApiToken", true),
			("gorang3.worklog", "CS:gorang3.worklog:ApiToken", true)));
		var sut = CreateSut();

		// Only Tempo was loaded this time, and the user disabled it.
		var saved = sut.Settings.Clone();
		saved.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>
		{
			["tempo.worklog"] = new() { ["ApiToken"] = "CS:tempo.worklog:ApiToken" },
		};
		saved.EnabledPlugins = new Dictionary<string, bool> { ["tempo.worklog"] = false };

		await sut.SaveSettingsAsync(saved, TestContext.Current.CancellationToken);

		var onDisk = JsonSerializer.Deserialize<ApplicationSettings>(
			await File.ReadAllTextAsync(Path.Combine(_settingsDir, "settings.json"), TestContext.Current.CancellationToken))!;
		onDisk.EnabledPlugins["tempo.worklog"].Should().BeFalse();
		onDisk.EnabledPlugins["gorang3.worklog"].Should().BeTrue();
	}

	[Fact]
	public void SaveSettings_NoPluginsInRequest_KeepsStoredPluginState()
	{
		WriteSettingsFile(SettingsWithPlugins(("tempo.worklog", "CS:tempo.worklog:ApiToken", true)));
		var sut = CreateSut();

		var saved = sut.Settings.Clone();
		saved.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>();
		saved.EnabledPlugins = new Dictionary<string, bool>();

		sut.SaveSettings(saved);

		var onDisk = JsonSerializer.Deserialize<ApplicationSettings>(
			File.ReadAllText(Path.Combine(_settingsDir, "settings.json")))!;
		onDisk.PluginConfigurations.Should().ContainKey("tempo.worklog");
		onDisk.EnabledPlugins["tempo.worklog"].Should().BeTrue();
	}

	/// <summary>
	/// A plugin the caller never saw must go back to disk in the form it was read in - not the
	/// decrypted one the service hands out in memory.
	/// </summary>
	[Fact]
	public async Task SaveSettingsAsync_PreservedPluginConfiguration_StaysProtected()
	{
		_mockSecureStorage.Setup(s => s.Unprotect("CS:tempo.worklog:ApiToken")).Returns("plaintext-token");
		WriteSettingsFile(SettingsWithPlugins(("tempo.worklog", "CS:tempo.worklog:ApiToken", true)));
		var sut = CreateSut();

		sut.Settings.PluginConfigurations["tempo.worklog"]["ApiToken"].Should().Be("plaintext-token");

		var saved = sut.Settings.Clone();
		saved.PluginConfigurations = new Dictionary<string, Dictionary<string, string>>();
		saved.EnabledPlugins = new Dictionary<string, bool>();

		await sut.SaveSettingsAsync(saved, TestContext.Current.CancellationToken);

		var raw = await File.ReadAllTextAsync(Path.Combine(_settingsDir, "settings.json"), TestContext.Current.CancellationToken);
		raw.Should().Contain("CS:tempo.worklog:ApiToken");
		raw.Should().NotContain("plaintext-token");
	}

	[Fact]
	public async Task SaveSettingsAsync_ExistingFile_KeepsPreviousContentsAsBackup()
	{
		WriteSettingsFile(SettingsWithPlugins(("tempo.worklog", "CS:tempo.worklog:ApiToken", true)));
		var sut = CreateSut();

		var saved = sut.Settings.Clone();
		saved.Theme = "Dark";
		await sut.SaveSettingsAsync(saved, TestContext.Current.CancellationToken);

		var backupPath = Path.Combine(_settingsDir, "settings.json.bak");
		File.Exists(backupPath).Should().BeTrue();
		JsonSerializer.Deserialize<ApplicationSettings>(await File.ReadAllTextAsync(backupPath, TestContext.Current.CancellationToken))!
			.Theme.Should().NotBe("Dark");
		File.Exists(Path.Combine(_settingsDir, "settings.json.tmp")).Should().BeFalse();
	}

	#endregion

	#region Environment-specific path

	[Fact]
	public void Constructor_CreatesEnvironmentSpecificDirectory()
	{
		CreateSut();

		Directory.Exists(_settingsDir).Should().BeTrue();
	}

	#endregion
}
