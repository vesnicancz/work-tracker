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
		_settingsDir = Application.WorkTrackerPaths.AppDataDirectory;

		// Default: pass-through for Unprotect
		_mockSecureStorage.Setup(s => s.Unprotect(It.IsAny<string>())).Returns((string v) => v);
	}

	public void Dispose()
	{
		// Clean up settings file created during test, but keep the directory
		// (it may be shared with other components using the same environment)
		var settingsFile = Path.Combine(_settingsDir, "settings.json");
		if (File.Exists(settingsFile))
		{
			File.Delete(settingsFile);
		}
	}

	private SettingsService CreateSut() =>
		new(_mockLogger.Object, _mockSecureStorage.Object);

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

	#region Environment-specific path

	[Fact]
	public void Constructor_CreatesEnvironmentSpecificDirectory()
	{
		CreateSut();

		Directory.Exists(_settingsDir).Should().BeTrue();
	}

	#endregion
}
