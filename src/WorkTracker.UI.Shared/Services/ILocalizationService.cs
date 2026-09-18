using System.ComponentModel;
using System.Globalization;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Interface for localization service
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
	/// <summary>
	/// Gets or sets the current culture
	/// </summary>
	CultureInfo CurrentCulture { get; set; }

	/// <summary>
	/// Gets the list of available cultures
	/// </summary>
	IEnumerable<CultureInfo> AvailableCultures { get; }

	/// <summary>
	/// Gets the operating system culture captured at startup, which the "system" language resolves to.
	/// Kept separately because setting <see cref="CurrentCulture"/> overwrites the ambient one.
	/// </summary>
	CultureInfo SystemCulture { get; }

	/// <summary>
	/// Gets the currently applied language code ("system", or a code from
	/// <see cref="Models.LanguageCatalog.SupportedLanguages"/>).
	/// </summary>
	string CurrentLanguage { get; }

	/// <summary>
	/// Applies a stored language code, switching the UI immediately. Unknown or empty values fall
	/// back to the system language rather than throwing.
	/// </summary>
	void ApplyLanguage(string? languageCode);

	/// <summary>
	/// Gets a localized string by key
	/// </summary>
	string GetString(string key);

	/// <summary>
	/// Gets a localized formatted string
	/// </summary>
	string GetFormattedString(string key, params object[] args);

	/// <summary>
	/// Indexer for convenient access to localized strings
	/// </summary>
	string this[string key] { get; }
}
