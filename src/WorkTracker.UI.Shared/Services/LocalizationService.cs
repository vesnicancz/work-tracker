using System.ComponentModel;
using System.Globalization;
using System.Resources;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Resources.Localization;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for managing application localization and culture changes
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
	/// <summary>
	/// CLR name of this type's string indexer. Avalonia's ReflectionIndexerNode - the node behind
	/// every {markup:Localize} binding - only re-reads when PropertyChanged names a declared indexed
	/// property, looked up with GetDeclaredProperty. It ignores the empty "everything changed" name,
	/// and "Item[]" finds no such property, so this exact name is what repaints the UI.
	/// </summary>
	private const string IndexerPropertyName = "Item";

	private readonly ResourceManager _resourceManager;
	private readonly CultureInfo _systemCulture;
	private CultureInfo _currentCulture;
	private string _currentLanguage = LanguageCatalog.SystemLanguage;

	/// <summary>
	/// Static instance for use in XAML markup extensions (which cannot use DI).
	/// Must be set via <see cref="SetInstance"/> before any XAML is loaded.
	/// </summary>
	public static LocalizationService Instance { get; private set; } = null!;

	public event PropertyChangedEventHandler? PropertyChanged;

	public LocalizationService()
	{
		_resourceManager = new ResourceManager(typeof(Strings));
		// Snapshot the OS culture before anything can overwrite CultureInfo.CurrentUICulture -
		// the CurrentCulture setter does exactly that, so this is the only chance to capture what
		// the "System" language option should resolve to for the rest of the process lifetime.
		_systemCulture = CultureInfo.CurrentUICulture;
		_currentCulture = _systemCulture;
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
	public CultureInfo SystemCulture => _systemCulture;

	/// <inheritdoc />
	public string CurrentLanguage => _currentLanguage;

	/// <inheritdoc />
	public void ApplyLanguage(string? languageCode)
	{
		_currentLanguage = LanguageCatalog.Normalize(languageCode);
		CurrentCulture = LanguageCatalog.ResolveCulture(_currentLanguage, _systemCulture);
	}

	/// <inheritdoc />
	public IEnumerable<CultureInfo> AvailableCultures => LanguageCatalog.SupportedCultures;

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
		// Indexer name first: this is what refreshes the XAML {markup:Localize} bindings.
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerPropertyName));
		// Empty name = "all properties changed" for plain property bindings and INPC consumers.
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
	}
}
