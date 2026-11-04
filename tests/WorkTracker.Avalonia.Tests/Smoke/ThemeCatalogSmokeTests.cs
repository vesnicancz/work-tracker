using Avalonia.Controls;
using FluentAssertions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Smoke;

public class ThemeCatalogSmokeTests
{
	/// <summary>
	/// Goes by the source folder, because compiled XAML doesn't show up among the assets at runtime.
	/// </summary>
	[Fact]
	public void Catalog_ListsEveryThemeFile()
	{
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while (!File.Exists(Path.Combine(root.FullName, "WorkTracker.slnx")))
		{
			root = root.Parent!;
		}

		var files = Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "WorkTracker.Avalonia", "Resources", "Themes"), "*.axaml")
			.Select(Path.GetFileName)
			.Where(name => name != "_Defaults.axaml");

		files.Should().BeEquivalentTo(ThemeCatalog.Themes.Select(t => t.FileName));
	}

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
