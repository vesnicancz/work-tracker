using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

/// <summary>
/// The expanded suggestions group lives in the settings file, so the dialog reopens on the same
/// group after a restart rather than only within one session.
/// </summary>
public class SuggestionsViewStateTests
{
	private readonly Mock<ISettingsService> _settingsService = new();
	private readonly ApplicationSettings _settings = new();
	private readonly SuggestionsViewState _sut;

	public SuggestionsViewStateTests()
	{
		_settingsService.Setup(s => s.Settings).Returns(_settings);
		_settingsService
			.Setup(s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
		_sut = new SuggestionsViewState(_settingsService.Object, NullLogger<SuggestionsViewState>.Instance);
	}

	[Fact]
	public void LastExpandedPluginId_ReadsTheStoredSetting()
	{
		_settings.LastExpandedSuggestionPluginId = "jira-suggestions";

		_sut.LastExpandedPluginId.Should().Be("jira-suggestions");
	}

	[Fact]
	public void LastExpandedPluginId_IsNullWhenNothingWasStored()
	{
		_sut.LastExpandedPluginId.Should().BeNull();
	}

	[Fact]
	public void SettingLastExpandedPluginId_PersistsIt()
	{
		_sut.LastExpandedPluginId = "o365-calendar";

		_settings.LastExpandedSuggestionPluginId.Should().Be("o365-calendar");
		_settingsService.Verify(
			s => s.SaveSettingsAsync(_settings, It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public void SettingTheSameValueAgain_DoesNotWriteTheSettingsFile()
	{
		_sut.LastExpandedPluginId = "o365-calendar";
		_sut.LastExpandedPluginId = "o365-calendar";

		_settingsService.Verify(
			s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public void AFailedSave_DoesNotEscapeToTheCaller()
	{
		_settingsService
			.Setup(s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new IOException("settings file is locked"));

		var expanding = () => _sut.LastExpandedPluginId = "o365-calendar";

		expanding.Should().NotThrow();
		_sut.LastExpandedPluginId.Should().Be("o365-calendar");
	}
}
