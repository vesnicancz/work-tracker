using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Helpers;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// The plugin configuration form is one DataTemplate for every field type, styled by classes the
/// field type switches on. Only the real SettingsWindow XAML shows whether those classes take
/// effect, so this renders it with one field of each kind that looks different.
/// </summary>
[Collection("LocalizationTests")]
public class SettingsWindowPluginFieldsSmokeTests
{
	private const int PluginsTabIndex = 3;

	private static SettingsViewModel CreateViewModel(ILocalizationService localization)
	{
		var plugin = MockPluginFactory.CreatePlugin("test.plugin", "Test Plugin",
			new PluginConfigurationField { Key = "Token", Label = "Token", Type = PluginConfigurationFieldType.Password, Description = "Your API token" },
			new PluginConfigurationField { Key = "Query", Label = "Query", Type = PluginConfigurationFieldType.MultilineText },
			new PluginConfigurationField { Key = "Url", Label = "Url", Type = PluginConfigurationFieldType.Url });

		var orchestrator = new Mock<ISettingsOrchestrator>();
		orchestrator.Setup(o => o.LoadPlugins()).Returns([new PluginViewModel(plugin.Object)]);

		var settings = new Mock<ISettingsService>();
		settings.SetupGet(s => s.Settings).Returns(new ApplicationSettings());

		return new SettingsViewModel(
			orchestrator.Object,
			settings.Object,
			NullLogger<SettingsViewModel>.Instance,
			new Mock<IAutostartManager>().Object,
			localization,
			new Mock<IThemeService>().Object);
	}

	[Fact]
	public Task PluginFields_RenderAccordingToTheirType() => UiThread.Dispatch(() =>
	{
		var originalInstance = LocalizationService.Instance;
		var localization = new LocalizationService();
		LocalizationService.SetInstance(localization);
		localization.ApplyLanguage("en");

		var window = new SettingsWindow { DataContext = CreateViewModel(localization) };
		window.Show();

		try
		{
			window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = PluginsTabIndex;
			window.UpdateLayout();

			var boxes = window.GetVisualDescendants().OfType<TextBox>()
				.Where(t => t.DataContext is ConfigurationFieldViewModel)
				.ToDictionary(t => ((ConfigurationFieldViewModel)t.DataContext!).Key);

			boxes["Token"].PasswordChar.Should().NotBe('\0', "a password field must be masked");
			boxes["Query"].AcceptsReturn.Should().BeTrue("a multi-line field must take Enter as a new line");
			boxes["Url"].PasswordChar.Should().Be('\0');
			boxes["Url"].AcceptsReturn.Should().BeFalse();

			window.GetVisualDescendants().OfType<TextBlock>()
				.Where(t => t.IsEffectivelyVisible)
				.Select(t => t.Text)
				.Should().Contain("Your API token", "the field description is the user's only hint for what goes in");
		}
		finally
		{
			window.Close();
			LocalizationService.SetInstance(originalInstance ?? new LocalizationService());
		}
	});
}
