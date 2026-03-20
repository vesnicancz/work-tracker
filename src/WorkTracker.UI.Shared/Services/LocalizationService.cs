using System.ComponentModel;
using System.Globalization;
using System.Resources;
using WorkTracker.UI.Shared.Resources.Localization;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for managing application localization and culture changes
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
	private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
	private readonly ResourceManager _resourceManager;
	private CultureInfo _currentCulture;

	public event PropertyChangedEventHandler? PropertyChanged;

	private LocalizationService()
	{
		_resourceManager = new ResourceManager(typeof(Strings));
		_currentCulture = CultureInfo.CurrentUICulture;
	}

	public static LocalizationService Instance => _instance.Value;

	/// <summary>
	/// Gets or sets the current culture
	/// </summary>
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

	/// <summary>
	/// Gets the list of available cultures
	/// </summary>
	public IEnumerable<CultureInfo> AvailableCultures => new[]
	{
		new CultureInfo("en"),
		new CultureInfo("cs")
	};

	/// <summary>
	/// Gets a localized string by key
	/// </summary>
	/// <param name="key">The resource key</param>
	/// <returns>The localized string</returns>
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

	/// <summary>
	/// Gets a localized formatted string
	/// </summary>
	/// <param name="key">The resource key</param>
	/// <param name="args">Format arguments</param>
	/// <returns>The formatted localized string</returns>
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

	/// <summary>
	/// Indexer for convenient access to localized strings
	/// </summary>
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
