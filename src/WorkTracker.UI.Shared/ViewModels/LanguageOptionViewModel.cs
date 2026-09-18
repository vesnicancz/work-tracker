using CommunityToolkit.Mvvm.ComponentModel;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// One entry in the language dropdown: the code that gets persisted, plus the label shown to the user.
/// </summary>
public sealed class LanguageOptionViewModel : ObservableObject
{
	private readonly ILocalizationService _localization;
	private readonly string? _resourceKey;

	/// <param name="resourceKey">
	/// Set only for the "System" entry, whose label is itself translated. Native language names are
	/// constants and need no key - which is also why <see cref="DisplayName"/> is computed on every
	/// get rather than captured: the System label has to follow a live language switch.
	/// </param>
	public LanguageOptionViewModel(string code, ILocalizationService localization, string? resourceKey = null)
	{
		Code = code;
		_localization = localization;
		_resourceKey = resourceKey;
	}

	/// <summary>Value persisted in settings.json.</summary>
	public string Code { get; }

	public string DisplayName =>
		_resourceKey is null ? LanguageCatalog.DisplayName(Code) : _localization[_resourceKey];

	/// <summary>Called after a language switch so the translated "System" label repaints.</summary>
	public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
}
