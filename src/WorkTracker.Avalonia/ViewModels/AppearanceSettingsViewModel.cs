using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// Theme and language in the Settings window. Both apply as soon as they are picked, and
/// <see cref="RevertPreview"/> puts them back when the dialog is dismissed.
/// </summary>
public class AppearanceSettingsViewModel : ViewModelBase
{
	private readonly ILocalizationService _localization;
	private readonly IThemeService _themeService;
	private string _selectedTheme;
	private bool _followSystemTheme;
	private string _selectedLightTheme;
	private string _selectedDarkTheme;
	private LanguageOptionViewModel _selectedLanguage;

	// Snapshot of everything previewed live, taken before the user can touch anything, so every
	// dismissal path can put the application back exactly where it found it.
	private readonly string _initialLanguage;
	private readonly bool _initialFollowSystemTheme;
	private readonly string _initialTheme;
	private readonly string _initialLightTheme;
	private readonly string _initialDarkTheme;

	public AppearanceSettingsViewModel(ApplicationSettings settings, ILocalizationService localization, IThemeService themeService)
	{
		_localization = localization;
		_themeService = themeService;

		_selectedTheme = settings.Theme ?? ApplicationSettings.DefaultTheme;
		_followSystemTheme = settings.FollowSystemTheme;
		_selectedLightTheme = ResolveLightTheme(settings.LightTheme);
		_selectedDarkTheme = ResolveDarkTheme(settings.DarkTheme);

		_initialFollowSystemTheme = _followSystemTheme;
		_initialTheme = _selectedTheme;
		_initialLightTheme = _selectedLightTheme;
		_initialDarkTheme = _selectedDarkTheme;

		_initialLanguage = LanguageCatalog.Normalize(settings.Language);
		AvailableLanguages =
		[
			new LanguageOptionViewModel(LanguageCatalog.SystemLanguage, localization, "LanguageSystem"),
			.. LanguageCatalog.SupportedLanguages.Select(code => new LanguageOptionViewModel(code, localization))
		];
		_selectedLanguage = AvailableLanguages.First(option => option.Code == _initialLanguage);
	}

	public string[] AvailableThemes { get; } = ThemeCatalog.AllThemes;

	public string[] AvailableLightThemes { get; } = ThemeCatalog.LightThemesSorted;

	public string[] AvailableDarkThemes { get; } = ThemeCatalog.DarkThemesSorted;

	public string SelectedTheme
	{
		get => _selectedTheme;
		set
		{
			if (SetProperty(ref _selectedTheme, value))
			{
				ApplyThemePreview();
			}
		}
	}

	public bool FollowSystemTheme
	{
		get => _followSystemTheme;
		set
		{
			if (SetProperty(ref _followSystemTheme, value))
			{
				OnPropertyChanged(nameof(IsSingleThemeMode));
				ApplyThemePreview();
			}
		}
	}

	public bool IsSingleThemeMode => !_followSystemTheme;

	public string SelectedLightTheme
	{
		get => _selectedLightTheme;
		set
		{
			if (SetProperty(ref _selectedLightTheme, value))
			{
				ApplyThemePreview();
			}
		}
	}

	public string SelectedDarkTheme
	{
		get => _selectedDarkTheme;
		set
		{
			if (SetProperty(ref _selectedDarkTheme, value))
			{
				ApplyThemePreview();
			}
		}
	}

	public IReadOnlyList<LanguageOptionViewModel> AvailableLanguages { get; }

	public LanguageOptionViewModel SelectedLanguage
	{
		get => _selectedLanguage;
		set
		{
			if (value is not null && SetProperty(ref _selectedLanguage, value))
			{
				ApplyLanguagePreview(value.Code);
			}
		}
	}

	/// <summary>
	/// Switches the UI language immediately, mirroring the theme live preview. Persisted only on
	/// Save; <see cref="RevertPreview"/> undoes it when the dialog is dismissed.
	/// </summary>
	private void ApplyLanguagePreview(string languageCode)
	{
		_localization.ApplyLanguage(languageCode);

		// The service repaints XAML {markup:Localize} bindings; this refreshes what is computed from
		// resources outside of them. The "everything changed" signal also tells SettingsViewModel.
		foreach (var option in AvailableLanguages)
		{
			option.RefreshDisplayName();
		}

		OnPropertyChanged(string.Empty);
	}

	/// <summary>
	/// Undoes every live preview the dialog applied - language and theme alike - restoring the
	/// state it opened with. Safe to call twice; each revert is a no-op when nothing changed.
	/// </summary>
	public void RevertPreview()
	{
		RevertLanguagePreview();
		RevertThemePreview();
	}

	private void RevertLanguagePreview()
	{
		if (SelectedLanguage.Code != _initialLanguage)
		{
			SelectedLanguage = AvailableLanguages.First(option => option.Code == _initialLanguage);
		}
	}

	/// <summary>
	/// Restores all four theme fields at once, then applies them in a single pass - going through
	/// the public setters instead would repaint the app up to four times on the way back.
	/// </summary>
	private void RevertThemePreview()
	{
		if (_followSystemTheme == _initialFollowSystemTheme
			&& _selectedTheme == _initialTheme
			&& _selectedLightTheme == _initialLightTheme
			&& _selectedDarkTheme == _initialDarkTheme)
		{
			return;
		}

		SetProperty(ref _followSystemTheme, _initialFollowSystemTheme, nameof(FollowSystemTheme));
		SetProperty(ref _selectedTheme, _initialTheme, nameof(SelectedTheme));
		SetProperty(ref _selectedLightTheme, _initialLightTheme, nameof(SelectedLightTheme));
		SetProperty(ref _selectedDarkTheme, _initialDarkTheme, nameof(SelectedDarkTheme));
		OnPropertyChanged(nameof(IsSingleThemeMode));

		ApplyThemePreview();
	}

	private void ApplyThemePreview()
	{
		_themeService.ApplyThemeMode(_followSystemTheme, _selectedTheme, _selectedLightTheme, _selectedDarkTheme);
	}

	private static string ResolveLightTheme(string? saved)
		=> !string.IsNullOrEmpty(saved) && ThemeCatalog.IsLight(saved) ? saved : ThemeCatalog.DefaultLightTheme;

	private static string ResolveDarkTheme(string? saved)
		=> !string.IsNullOrEmpty(saved) && !ThemeCatalog.IsLight(saved) ? saved : ThemeCatalog.DefaultDarkTheme;
}
