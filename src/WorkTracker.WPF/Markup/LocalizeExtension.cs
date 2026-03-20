using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using WorkTracker.WPF.Services;

namespace WorkTracker.WPF.Markup;

/// <summary>
/// Markup extension for localized strings in XAML
/// Usage: {Localize Key}
/// Example: Text="{Localize AppTitle}"
/// </summary>
[MarkupExtensionReturnType(typeof(BindingExpression))]
public class LocalizeExtension : MarkupExtension
{
	public LocalizeExtension()
	{
	}

	public LocalizeExtension(string key)
	{
		Key = key;
	}

	[ConstructorArgument("key")]
	public string? Key { get; set; }

	public override object ProvideValue(IServiceProvider serviceProvider)
	{
		if (string.IsNullOrEmpty(Key))
		{
			return "[No Key]";
		}

		// For design-time support
		if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
		{
			return $"[{Key}]";
		}

		// Create a binding to the localization service
		var binding = new Binding($"[{Key}]")
		{
			Source = LocalizationService.Instance,
			Mode = BindingMode.OneWay
		};

		if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target)
		{
			if (target.TargetObject is DependencyObject dependencyObject &&
				target.TargetProperty is DependencyProperty dependencyProperty)
			{
				return binding.ProvideValue(serviceProvider);
			}
		}

		return binding;
	}
}
