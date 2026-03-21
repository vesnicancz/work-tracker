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
