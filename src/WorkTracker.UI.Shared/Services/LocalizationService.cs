using System.ComponentModel;
using System.Globalization;
using System.Resources;
using WorkTracker.UI.Shared.Resources.Localization;

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

/// <summary>
/// Service for managing application localization and culture changes
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
	private readonly ResourceManager _resourceManager;
	private CultureInfo _currentCulture;

	/// <summary>
	/// Static instance for use in XAML markup extensions (which cannot use DI).
	/// Set automatically during DI registration.
	/// </summary>
	public static LocalizationService Instance { get; private set; } = new();

	public event PropertyChangedEventHandler? PropertyChanged;

	public LocalizationService()
	{
		_resourceManager = new ResourceManager(typeof(Strings));
		_currentCulture = CultureInfo.CurrentUICulture;
	}

	/// <summary>
	/// Registers this instance as the static Instance (called from DI setup).
	/// </summary>
	public static void SetInstance(LocalizationService instance) => Instance = instance;

	/// <inheritdoc />
	public CultureInfo CurrentCulture
	{
		get => _currentCulture;
		set
		{
			if (!Equals(_currentCulture, value))
			{
				_currentCulture = value;
				CultureInfo.CurrentUICulture = value;
				CultureInfo.CurrentCulture = value;
				OnPropertyChanged(nameof(CurrentCulture));
				OnLanguageChanged();
			}
		}
	}

	/// <inheritdoc />
	public IEnumerable<CultureInfo> AvailableCultures => new[]
	{
		new CultureInfo("en"),
		new CultureInfo("cs")
	};

	/// <inheritdoc />
	public string GetString(string key)
	{
		try
		{
			var value = _resourceManager.GetString(key, _currentCulture);
			return value ?? $"[{key}]";
		}
		catch
		{
			return $"[{key}]";
		}
	}

	/// <inheritdoc />
	public string GetFormattedString(string key, params object[] args)
	{
		try
		{
			var format = GetString(key);
			return string.Format(format, args);
		}
		catch
		{
			return $"[{key}]";
		}
	}

	/// <inheritdoc />
	public string this[string key] => GetString(key);

	private void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	private void OnLanguageChanged()
	{
		// Notify all subscribers that language has changed
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
	}
}
