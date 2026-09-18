using System.Globalization;
using FluentAssertions;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Tests.Models;

public class LanguageCatalogTests
{
	#region Normalize

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("system")]
	[InlineData("SYSTEM")]
	[InlineData("de")]
	[InlineData("en-US")]
	[InlineData("garbage")]
	public void Normalize_UnknownOrSystemValue_ReturnsSystemLanguage(string? input)
	{
		LanguageCatalog.Normalize(input).Should().Be(LanguageCatalog.SystemLanguage);
	}

	[Theory]
	[InlineData("cs", "cs")]
	[InlineData("CS", "cs")]
	[InlineData(" en ", "en")]
	public void Normalize_SupportedLanguage_ReturnsCanonicalCode(string input, string expected)
	{
		LanguageCatalog.Normalize(input).Should().Be(expected);
	}

	#endregion

	#region ResolveCulture

	[Fact]
	public void ResolveCulture_System_ReturnsSystemCultureVerbatim()
	{
		var system = new CultureInfo("cs-CZ");

		// Region-specific culture is kept as-is; ResourceManager falls back cs-CZ -> cs -> neutral.
		LanguageCatalog.ResolveCulture(LanguageCatalog.SystemLanguage, system).Should().Be(system);
	}

	[Fact]
	public void ResolveCulture_ExplicitLanguage_IgnoresSystemCulture()
	{
		var culture = LanguageCatalog.ResolveCulture("en", new CultureInfo("cs-CZ"));

		culture.TwoLetterISOLanguageName.Should().Be("en");
	}

	[Fact]
	public void ResolveCulture_UnknownLanguage_FallsBackToSystemNotEnglish()
	{
		var system = new CultureInfo("de-DE");

		// Unknown codes follow the OS. English only ever appears via the neutral resource fallback.
		LanguageCatalog.ResolveCulture("zz", system).Should().Be(system);
	}

	#endregion

	#region Catalog contents

	[Fact]
	public void SupportedLanguages_AreCzechAndEnglish()
	{
		LanguageCatalog.SupportedLanguages.Should().Equal("cs", "en");
	}

	[Fact]
	public void SupportedCultures_MatchSupportedLanguages()
	{
		LanguageCatalog.SupportedCultures.Select(c => c.Name)
			.Should().Equal(LanguageCatalog.SupportedLanguages);
	}

	[Theory]
	[InlineData("cs", "Čeština")]
	[InlineData("en", "English")]
	public void DisplayName_ReturnsNativeName(string code, string expected)
	{
		LanguageCatalog.DisplayName(code).Should().Be(expected);
	}

	[Fact]
	public void DisplayName_UnknownCode_ReturnsCodeItself()
	{
		LanguageCatalog.DisplayName("zz").Should().Be("zz");
	}

	#endregion
}
