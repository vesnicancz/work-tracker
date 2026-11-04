using Avalonia.Platform;
using FluentAssertions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Smoke;

public class ThemeCatalogSmokeTests
{
	[Fact]
	public Task Catalog_ListsEveryThemeFile() => UiThread.Dispatch(() =>
	{
		var files = AssetLoader.GetAssets(new Uri("avares://WorkTracker.Avalonia/Resources/Themes"), null)
			.Select(uri => Path.GetFileName(uri.AbsolutePath))
			.Where(name => name != "_Defaults.axaml");

		files.Should().BeEquivalentTo(ThemeCatalog.Themes.Select(t => t.FileName));
	});

	[Fact]
	public void Find_UnknownName_GivesTheDefaultDarkTheme()
	{
		ThemeCatalog.Find("Removed long ago").Name.Should().Be(ThemeCatalog.DefaultDarkTheme);
	}

	[Fact]
	public Task SwitchTheme_UnknownName_StillLoadsATheme() => UiThread.Dispatch(() =>
	{
		ThemeManager.SwitchTheme("Removed long ago");

		global::Avalonia.Application.Current!.TryFindResource("CardBackground", out _).Should().BeTrue();
	});
}
