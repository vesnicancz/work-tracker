using System.Globalization;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// End-to-end cover for the language selector: the real SettingsWindow XAML, bound to a real
/// LocalizationService, has to repaint itself when the dropdown changes. Exercises the whole chain
/// (ComboBox -> ViewModel -> service -> {markup:Localize} bindings) that unit tests can only stub.
/// </summary>
[Collection("LocalizationTests")]
public class SettingsWindowLanguageSmokeTests
{
	private static SettingsViewModel CreateViewModel(ILocalizationService localization, string language)
	{
		var orchestrator = new Mock<ISettingsOrchestrator>();
		orchestrator.Setup(o => o.LoadPlugins()).Returns([]);

		var settings = new Mock<ISettingsService>();
		settings.SetupGet(s => s.Settings).Returns(new ApplicationSettings { Language = language });

		return new SettingsViewModel(
			orchestrator.Object,
			settings.Object,
			NullLogger<SettingsViewModel>.Instance,
			new Mock<IAutostartManager>().Object,
			localization,
			new Mock<IThemeService>().Object);
	}

	[Fact]
	public Task SettingsWindow_SwitchingLanguage_RepaintsLocalizedText() => UiThread.Dispatch(() =>
	{
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUICulture = CultureInfo.CurrentUICulture;
		var originalInstance = LocalizationService.Instance;

		var localization = new LocalizationService();
		LocalizationService.SetInstance(localization);
		localization.ApplyLanguage("en");

		var viewModel = CreateViewModel(localization, "en");
		var window = new SettingsWindow { DataContext = viewModel };
		window.Show();

		try
		{
			window.Title.Should().Be("Settings");

			viewModel.SelectedLanguage = viewModel.AvailableLanguages.First(o => o.Code == "cs");

			window.Title.Should().Be("Nastavení", "the open window must follow a live language switch");
			localization.CurrentLanguage.Should().Be("cs");

			// The "System" entry is the one translated label in the dropdown, so it must refresh too.
			viewModel.AvailableLanguages
				.First(o => o.Code == LanguageCatalog.SystemLanguage).DisplayName
				.Should().Be("Systém");

			// DisplayMemberBinding resolves against the item, not the window's DataContext - if it
			// ever broke, the ComboBox would quietly render type names instead of language names.
			var comboBox = window.GetVisualDescendants().OfType<ComboBox>()
				.Single(c => ReferenceEquals(c.ItemsSource, viewModel.AvailableLanguages));
			window.UpdateLayout();

			comboBox.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
				.Should().Contain("Čeština", "the selection box renders through DisplayMemberBinding");
		}
		finally
		{
			window.Close();
			LocalizationService.SetInstance(originalInstance ?? new LocalizationService());
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUICulture;
		}
	});
}
