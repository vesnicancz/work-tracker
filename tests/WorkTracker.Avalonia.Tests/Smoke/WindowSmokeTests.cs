using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using FluentAssertions;
using Moq;
using WorkTracker.Avalonia.Tests.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Tests.Common.Builders;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

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

	/// <summary>
	/// With StartMinimized the window is wired up but never shown on Linux and macOS, because
	/// mapping and immediately unmapping a toplevel leaves a stale task manager entry. Everything
	/// Initialize touches has to work on a window that has no mapped surface yet.
	/// </summary>
	[Fact]
	public Task MainWindow_Initialize_SucceedsOnAWindowThatWasNeverShown() => UiThread.Dispatch(() =>
	{
		var harness = new MainViewModelHarness();
		harness.Settings.SetupGet(s => s.Settings).Returns(new ApplicationSettings { StartMinimized = true });
		var tray = new Mock<ITrayIconService>();

		var window = new MainWindow();
		using var viewModel = harness.CreateViewModel();

		window.Initialize(viewModel, tray.Object, harness.Settings.Object);

		window.IsVisible.Should().BeFalse();
		window.FindControl<StackPanel>("LoadingPanel")!.IsVisible.Should().BeFalse();
		window.FindControl<Grid>("MainContent")!.IsVisible.Should().BeTrue();
		window.FindControl<StackPanel>("NotificationHost").Should().NotBeNull();
		window.DataContext.Should().BeSameAs(viewModel);
		tray.Verify(t => t.Initialize(), Times.Once);
		tray.Verify(t => t.Show(), Times.Once);
	});

	/// <summary>
	/// Starting minimized pauses the timer, so the first show — which now happens only when the user
	/// opens the window from the tray — has to resume it.
	/// </summary>
	[Fact]
	public Task MainWindow_FirstShowAfterInitialize_ResumesTimer() => UiThread.Dispatch(() =>
	{
		var harness = new MainViewModelHarness();
		harness.Settings.SetupGet(s => s.Settings).Returns(new ApplicationSettings { StartMinimized = true });
		harness.WorklogState.SetupGet(w => w.IsTracking).Returns(true);
		harness.WorklogState.SetupGet(w => w.ActiveWork)
			.Returns(new WorkEntryBuilder().WithId(1).WithStartTime(MainViewModelHarness.LocalNow.AddHours(-1)).Build());

		var window = new MainWindow();
		using var viewModel = harness.CreateViewModel();
		window.Initialize(viewModel, new Mock<ITrayIconService>().Object, harness.Settings.Object);

		viewModel.ElapsedTime.Should().Be("00:00:00", "starting minimized pauses the timer");

		window.Show();

		try
		{
			viewModel.ElapsedTime.Should().Be("01:00:00");
		}
		finally
		{
			window.Close();
		}
	});

	[Fact]
	public Task MainWindow_ClosingWithMinimizeToTray_CancelsTheCloseAndHides() => UiThread.Dispatch(() =>
	{
		var harness = new MainViewModelHarness();
		harness.Settings.SetupGet(s => s.Settings)
			.Returns(new ApplicationSettings { CloseWindowBehavior = CloseWindowBehavior.MinimizeToTray });
		var tray = new Mock<ITrayIconService>();

		var window = new MainWindow();
		using var viewModel = harness.CreateViewModel();
		window.Initialize(viewModel, tray.Object, harness.Settings.Object);
		window.Show();

		window.Close();

		window.IsVisible.Should().BeFalse();
		tray.Verify(t => t.Show(), Times.AtLeastOnce);

		// The window was hidden, not closed — it can be shown again.
		window.Show();
		window.IsVisible.Should().BeTrue();
		window.Hide();
	});

	/// <summary>
	/// Clicking the tray icon hides the window instead of minimizing it, so nothing is left behind in
	/// the task bar.
	/// </summary>
	[Fact]
	public Task MainWindow_HideToTray_HidesTheWindowAndPausesTheTimer() => UiThread.Dispatch(() =>
	{
		var harness = new MainViewModelHarness();
		harness.WorklogState.SetupGet(w => w.IsTracking).Returns(true);
		harness.WorklogState.SetupGet(w => w.ActiveWork)
			.Returns(new WorkEntryBuilder().WithId(1).WithStartTime(MainViewModelHarness.LocalNow.AddHours(-1)).Build());
		var tray = new Mock<ITrayIconService>();

		var window = new MainWindow();
		using var viewModel = harness.CreateViewModel();
		window.Initialize(viewModel, tray.Object, harness.Settings.Object);
		window.Show();
		viewModel.ElapsedTime.Should().Be("01:00:00");

		window.HideToTray();

		window.IsVisible.Should().BeFalse();
		window.WindowState.Should().NotBe(WindowState.Minimized, "minimizing would leave a task bar entry");
		tray.Verify(t => t.Show(), Times.AtLeastOnce);

		// Showing it again resumes the display timer.
		window.Show();
		viewModel.ElapsedTime.Should().Be("01:00:00");
		window.Hide();
	});

	[Theory]
	// Only a user-initiated close may be turned into a hide.
	[InlineData(WindowCloseReason.WindowClosing, CloseWindowBehavior.MinimizeToTray, true)]
	[InlineData(WindowCloseReason.WindowClosing, CloseWindowBehavior.ExitApplication, false)]
	// Cancelling these would abort the shutdown and block OS power-off on Linux.
	[InlineData(WindowCloseReason.ApplicationShutdown, CloseWindowBehavior.MinimizeToTray, false)]
	[InlineData(WindowCloseReason.OSShutdown, CloseWindowBehavior.MinimizeToTray, false)]
	[InlineData(WindowCloseReason.OwnerWindowClosing, CloseWindowBehavior.MinimizeToTray, false)]
	public void ShouldMinimizeToTray_OnlyForUserInitiatedCloses(
		WindowCloseReason reason, CloseWindowBehavior behavior, bool expected)
	{
		MainWindow.ShouldMinimizeToTray(reason, behavior).Should().Be(expected);
	}

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
