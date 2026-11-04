using FluentAssertions;
using WorkTracker.Avalonia.Models;

namespace WorkTracker.Avalonia.Tests.Models;

public sealed class EarlySettingsTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"early-settings-{Guid.NewGuid():N}.json");

	public void Dispose()
	{
		File.Delete(_path);
	}

	private EarlySettings ReadFrom(string json)
	{
		File.WriteAllText(_path, json);
		return EarlySettings.Read(_path);
	}

	[Fact]
	public void MissingFile_GivesDefaults()
	{
		var settings = EarlySettings.Read(_path);

		settings.Theme.Should().Be(ApplicationSettings.DefaultTheme);
		settings.StartMinimized.Should().BeFalse();
		settings.Language.Should().Be(LanguageCatalog.SystemLanguage);
	}

	[Fact]
	public void BrokenFile_GivesDefaults()
	{
		ReadFrom("{ not json").Theme.Should().Be(ApplicationSettings.DefaultTheme);
	}

	[Fact]
	public void ReadsWhatIsThere()
	{
		var settings = ReadFrom("""{"Theme":"Abyss","FollowSystemTheme":true,"LightTheme":"Coral","DarkTheme":"Eclipse","StartMinimized":true,"Language":"cs"}""");

		settings.Should().Be(new EarlySettings("Abyss", true, "Coral", "Eclipse", true, "cs"));
	}

	[Fact]
	public void LanguageOfTheWrongType_KeepsTheTheme()
	{
		var settings = ReadFrom("""{"Theme":"Abyss","Language":5}""");

		settings.Theme.Should().Be("Abyss");
		settings.Language.Should().Be(LanguageCatalog.SystemLanguage);
	}
}
