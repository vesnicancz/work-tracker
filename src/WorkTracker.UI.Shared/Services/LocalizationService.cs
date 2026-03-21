using System.ComponentModel;
using System.Globalization;
using System.Resources;
using WorkTracker.UI.Shared.Resources.Localization;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for managing application localization and culture changes
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
	private readonly ResourceManager _resourceManager;
	private CultureInfo _currentCulture;

	/// <summary>
	/// Static instance for use in XAML markup extensions (which cannot use DI).
	/// Must be set via <see cref="SetInstance"/> before any XAML is loaded.
	/// </summary>
	public static LocalizationService Instance { get; private set; } = null!;

	public event PropertyChangedEventHandler? PropertyChanged;

	public LocalizationService()
	{
		_resourceManager = new ResourceManager(typeof(Strings));
		_currentCulture = CultureInfo.CurrentUICulture;
	}

	/// <summary>
	/// Sets the singleton instance used by XAML markup extensions.
	/// Must be called before any window/XAML is created.
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
