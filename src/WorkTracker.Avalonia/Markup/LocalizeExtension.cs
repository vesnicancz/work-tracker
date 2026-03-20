using Avalonia.Data;
using Avalonia.Markup.Xaml;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Markup;

/// <summary>
/// Markup extension for localized strings in AXAML.
/// Usage: Text="{markup:Localize AppTitle}"
/// Binds to LocalizationService indexer so the UI updates when language changes.
/// </summary>
public class LocalizeExtension : MarkupExtension
{
	public LocalizeExtension()
	{
	}

	public LocalizeExtension(string key)
	{
		Key = key;
	}

	public string? Key { get; set; }

	public override object ProvideValue(IServiceProvider serviceProvider)
	{
		if (string.IsNullOrEmpty(Key))
		{
			return "[No Key]";
		}

		// Create a binding to the LocalizationService indexer
		var binding = new Binding($"[{Key}]")
		{
			Source = LocalizationService.Instance,
			Mode = BindingMode.OneWay
		};

		return binding;
	}
}
