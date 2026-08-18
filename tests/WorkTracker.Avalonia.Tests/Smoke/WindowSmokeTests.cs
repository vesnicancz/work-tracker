using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using FluentAssertions;
using WorkTracker.Avalonia.Views;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.Avalonia.Tests.Smoke;

public class WindowSmokeTests
{
	[Fact]
	public Task MainWindow_CanBeConstructedAndShown() => UiThread.Dispatch(() =>
	{
		var window = new MainWindow();

		window.Show();
		window.Close();
	});

	[Fact]
	public Task MessageBoxWindow_CanBeConstructedAndShown() => UiThread.Dispatch(() =>
	{
		var window = new MessageBoxWindow("Title", "Message");

		window.Show();
		window.Close();
	});

	[Theory]
	[InlineData(MessageBoxButtons.Ok, "OkPanel")]
	[InlineData(MessageBoxButtons.YesNo, "YesNoPanel")]
	[InlineData(MessageBoxButtons.RetryClose, "RetryPanel")]
	public Task MessageBoxWindow_ShowsOnlyThePanelForTheRequestedButtons(MessageBoxButtons buttons, string expectedPanel)
		=> UiThread.Dispatch(() =>
		{
			var window = new MessageBoxWindow("Title", "Message", buttons);
			window.Show();

			try
			{
				foreach (var panelName in new[] { "OkPanel", "YesNoPanel", "RetryPanel" })
				{
					var panel = window.FindControl<StackPanel>(panelName);
					panel.Should().NotBeNull();
					panel!.IsVisible.Should().Be(panelName == expectedPanel, $"'{panelName}' visibility for {buttons}");
				}
			}
			finally
			{
				window.Close();
			}
		});

	[Fact]
	public Task MessageBoxWindow_RetryButton_ReportsAffirmativeResult() => UiThread.Dispatch(() =>
	{
		var window = new MessageBoxWindow("Title", "Message", MessageBoxButtons.RetryClose);
		window.Show();

		var retryButton = window.FindControl<Button>("RetryButton");
		retryButton.Should().NotBeNull();
		retryButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

		window.Result.Should().BeTrue();
	});

	[Fact]
	public Task MessageBoxWindow_CloseApplicationButton_ReportsNegativeResult() => UiThread.Dispatch(() =>
	{
		var window = new MessageBoxWindow("Title", "Message", MessageBoxButtons.RetryClose);
		window.Show();

		var closeAppButton = window.FindControl<Button>("CloseAppButton");
		closeAppButton.Should().NotBeNull();
		closeAppButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

		window.Result.Should().BeFalse();
	});

	[Fact]
	public Task SwitchTheme_AllCatalogThemes_LoadAndSetMatchingVariant() => UiThread.Dispatch(() =>
	{
		foreach (var themeName in ThemeCatalog.AllThemes)
		{
			App.SwitchTheme(themeName);

			var app = global::Avalonia.Application.Current!;
			var expected = ThemeCatalog.IsLight(themeName) ? ThemeVariant.Light : ThemeVariant.Dark;
			app.RequestedThemeVariant.Should().Be(expected, $"theme '{themeName}' should apply its variant");
		}
	});
}
