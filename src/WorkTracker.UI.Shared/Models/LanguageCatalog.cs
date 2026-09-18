using System.Globalization;

namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Central registry of the shipped UI languages and the rules that turn the persisted
/// <see cref="ApplicationSettings.Language"/> code into a culture. Mirrors <see cref="ThemeCatalog"/>:
/// the early-startup path in App.axaml.cs and the settings UI both resolve through here, so they
/// can never disagree about what a stored value means.
/// </summary>
public static class LanguageCatalog
{
	/// <summary>
	/// Stored in settings.json when the UI language should follow the operating system.
	/// This is the default, so the app behaves exactly as it did before the selector existed.
	/// </summary>
	public const string SystemLanguage = "system";

	/// <summary>
	/// Shipped translations, in dropdown order. "System" is prepended by the settings ViewModel.
	/// Adding a language means a new Strings.&lt;code&gt;.resx plus an entry here and in <see cref="DisplayName"/>.
	/// </summary>
	public static readonly string[] SupportedLanguages = ["cs", "en"];

	public static readonly CultureInfo[] SupportedCultures =
		[.. SupportedLanguages.Select(code => new CultureInfo(code))];

	/// <summary>
	/// Maps null, empty, "system" and anything unrecognised (a hand-edited file, or a language
	/// dropped in a later version) to <see cref="SystemLanguage"/>, so a bad value degrades
	/// quietly instead of throwing.
	/// </summary>
	public static string Normalize(string? languageCode)
	{
		if (string.IsNullOrWhiteSpace(languageCode))
		{
			return SystemLanguage;
		}

		var trimmed = languageCode.Trim();
		return Array.Find(
			SupportedLanguages,
			code => string.Equals(code, trimmed, StringComparison.OrdinalIgnoreCase)) ?? SystemLanguage;
	}

	/// <summary>
	/// Resolves a stored code to the culture to display. "system" returns <paramref name="systemCulture"/>
	/// verbatim (e.g. cs-CZ), which is what the app used before the selector existed - ResourceManager
	/// then falls back cs-CZ to cs to the neutral resources (English).
	/// </summary>
	public static CultureInfo ResolveCulture(string? languageCode, CultureInfo systemCulture)
	{
		var normalized = Normalize(languageCode);
		return normalized == SystemLanguage ? systemCulture : new CultureInfo(normalized);
	}

	/// <summary>
	/// Native language name, intentionally left untranslated - someone looking for English
	/// recognises "English" even while the UI is in Czech.
	/// </summary>
	public static string DisplayName(string languageCode) => languageCode switch
	{
		"cs" => "Čeština",
		"en" => "English",
		_ => languageCode
	};
}
