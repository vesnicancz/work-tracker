using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Services;
using WorkTracker.Infrastructure.Settings;

namespace WorkTracker.Infrastructure.Tests.Settings;

public sealed class PluginSettingsReaderTests : IDisposable
{
	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), $"wt-plugin-settings-{Guid.NewGuid():N}");

	private readonly Mock<ISecureStorage> _secureStorage = new();

	private string SettingsPath => Path.Combine(_directory, "settings.json");

	public PluginSettingsReaderTests()
	{
		Directory.CreateDirectory(_directory);
		_secureStorage.Setup(s => s.Unprotect(It.IsAny<string>())).Returns<string>(value => value);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_directory, recursive: true);
		}
		catch (DirectoryNotFoundException)
		{
			// Already gone — nothing to clean up.
		}
	}

	private PluginSettingsReader CreateReader() =>
		new(_secureStorage.Object, NullLogger<PluginSettingsReader>.Instance, SettingsPath);

	private void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

	[Fact]
	public async Task ReadAsync_ReadsEnabledPluginsAndConfigurations()
	{
		WriteSettings("""
		{
		  "EnabledPlugins": { "tempo.worklog": true, "jira.suggestions": false },
		  "PluginConfigurations": { "tempo.worklog": { "TempoBaseUrl": "https://api.tempo.io/4" } }
		}
		""");

		var settings = await CreateReader().ReadAsync(TestContext.Current.CancellationToken);

		settings.EnabledPlugins.Should().HaveCount(2);
		settings.EnabledPlugins["tempo.worklog"].Should().BeTrue();
		settings.EnabledPlugins["jira.suggestions"].Should().BeFalse();
		settings.PluginConfigurations["tempo.worklog"]["TempoBaseUrl"].Should().Be("https://api.tempo.io/4");
	}

	[Fact]
	public async Task ReadAsync_IgnoresSettingsOwnedByTheGui()
	{
		// The CLI must not need to know the full settings model — unrelated properties, including
		// shapes it has no type for, are simply skipped.
		WriteSettings("""
		{
		  "Theme": "Modern Blue",
		  "Pomodoro": { "Enabled": true, "WorkMinutes": 25 },
		  "FavoriteWorkItems": [ { "Id": "1", "Name": "x" } ],
		  "EnabledPlugins": { "tempo.worklog": true }
		}
		""");

		var settings = await CreateReader().ReadAsync(TestContext.Current.CancellationToken);

		settings.EnabledPlugins.Should().ContainKey("tempo.worklog");
	}

	[Fact]
	public async Task ReadAsync_UnprotectsConfigurationValues()
	{
		_secureStorage.Setup(s => s.Unprotect("protected:token")).Returns("real-token");
		WriteSettings("""
		{
		  "PluginConfigurations": { "tempo.worklog": { "ApiToken": "protected:token", "Url": "https://x" } }
		}
		""");

		var settings = await CreateReader().ReadAsync(TestContext.Current.CancellationToken);

		settings.PluginConfigurations["tempo.worklog"]["ApiToken"].Should().Be("real-token");
		settings.PluginConfigurations["tempo.worklog"]["Url"].Should().Be("https://x");
	}

	[Fact]
	public async Task ReadAsync_MissingFile_ReturnsEmptySettings()
	{
		var settings = await CreateReader().ReadAsync(TestContext.Current.CancellationToken);

		settings.EnabledPlugins.Should().BeEmpty();
		settings.PluginConfigurations.Should().BeEmpty();
	}

	[Fact]
	public async Task ReadAsync_MalformedFile_ReturnsEmptySettingsWithoutThrowing()
	{
		// A broken settings file must not stop an unrelated command from running.
		WriteSettings("{ this is not json");

		var settings = await CreateReader().ReadAsync(TestContext.Current.CancellationToken);

		settings.EnabledPlugins.Should().BeEmpty();
		settings.PluginConfigurations.Should().BeEmpty();
	}

	[Fact]
	public async Task ReadAsync_NullPluginConfigurationEntry_DoesNotThrow()
	{
		WriteSettings("""
		{ "PluginConfigurations": { "tempo.worklog": null } }
		""");

		var settings = await CreateReader().ReadAsync(TestContext.Current.CancellationToken);

		settings.PluginConfigurations.Should().ContainKey("tempo.worklog");
	}
}
