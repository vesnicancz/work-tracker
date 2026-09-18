using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using FluentAssertions;
using WorkTracker.Avalonia.Markup;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Tests.Markup;

/// <summary>
/// Guards the live language-switch mechanism: <see cref="LocalizeExtension"/> binds to the
/// localization service's indexer, and <see cref="LocalizationService"/> signals a language
/// change with an empty PropertyName. If Avalonia ever stops refreshing indexer bindings on
/// that signal, every {markup:Localize} string in the app silently goes stale.
/// </summary>
[Collection("LocalizationTests")]
public class LocalizeExtensionTests
{
	[Fact]
	public Task Binding_RefreshesWhenCultureChanges() => UiThread.Dispatch(() =>
	{
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUICulture = CultureInfo.CurrentUICulture;
		var originalInstance = LocalizationService.Instance;

		try
		{
			var localization = new LocalizationService();
			LocalizationService.SetInstance(localization);
			localization.CurrentCulture = new CultureInfo("en");

			var binding = (Binding)new LocalizeExtension("Settings").ProvideValue(null!);
			var textBlock = new TextBlock();
			textBlock.Bind(TextBlock.TextProperty, binding);

			var english = textBlock.Text;
			english.Should().NotBeNullOrEmpty();
			english.Should().NotStartWith("[");

			localization.CurrentCulture = new CultureInfo("cs");

			textBlock.Text.Should().NotBe(english, "the binding must refresh when the language changes");
			textBlock.Text.Should().NotStartWith("[");
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUICulture;
			LocalizationService.SetInstance(originalInstance ?? new LocalizationService());
		}
	});
}
