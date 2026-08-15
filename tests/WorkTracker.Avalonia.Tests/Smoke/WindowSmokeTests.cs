using Avalonia.Styling;
using FluentAssertions;
using WorkTracker.Avalonia.Views;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.Avalonia.Tests.Smoke;

public class WindowSmokeTests
{
	[Fact]
	public Task MainWindow_CanBeConstructedAndShown() => UiThread.Dispatch(() =>
	{
		var window = new MainWindow();

		window.Show();
		window.Close();
	});

	[Fact]
	public Task MessageBoxWindow_CanBeConstructedAndShown() => UiThread.Dispatch(() =>
	{
		var window = new MessageBoxWindow("Title", "Message", false);

		window.Show();
		window.Close();
	});

	[Fact]
	public Task SwitchTheme_AllCatalogThemes_LoadAndSetMatchingVariant() => UiThread.Dispatch(() =>
	{
		foreach (var themeName in ThemeCatalog.AllThemes)
		{
			App.SwitchTheme(themeName);

			var app = global::Avalonia.Application.Current!;
			var expected = ThemeCatalog.IsLight(themeName) ? ThemeVariant.Light : ThemeVariant.Dark;
			app.RequestedThemeVariant.Should().Be(expected, $"theme '{themeName}' should apply its variant");
		}
	});
}
