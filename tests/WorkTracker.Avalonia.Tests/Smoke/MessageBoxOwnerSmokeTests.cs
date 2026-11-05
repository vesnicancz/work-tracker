using Avalonia.Controls;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// With the application in the tray there is no visible main window to own a message box, and Avalonia
/// refuses a hidden owner. The question about an overlap has to reach the user anyway.
/// </summary>
public class MessageBoxOwnerSmokeTests
{
	private static DialogService CreateDialogService(Window? mainWindow = null)
	{
		var localization = new Mock<ILocalizationService>();
		localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);

		var services = new ServiceCollection();
		services.AddSingleton(Mock.Of<IWorkEntryEditOrchestrator>());
		services.AddSingleton(localization.Object);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
		services.AddTransient<WorkEntryEditViewModel>();
		var provider = services.BuildServiceProvider();

		return new DialogService(
			provider.GetRequiredService<IServiceScopeFactory>(),
			Mock.Of<IMainWindowAccessor>(a => a.MainWindow == mainWindow),
			NullLogger<DialogService>.Instance);
	}

	[Fact]
	public Task Confirmation_WithoutVisibleMainWindow_IsShownOnItsOwn() => UiThread.Dispatch(async () =>
	{
		var dialogService = CreateDialogService();

		var answer = dialogService.ShowConfirmationAsync("message");

		var messageBox = dialogService.OpenMessageBox;
		messageBox.Should().NotBeNull();
		messageBox!.IsVisible.Should().BeTrue();
		messageBox.Owner.Should().BeNull();
		answer.IsCompleted.Should().BeFalse();

		messageBox.Close();

		(await answer).Should().BeFalse();
		dialogService.OpenMessageBox.Should().BeNull();
	});

	[Fact]
	public Task Confirmation_WithHiddenMainWindow_IsShownOnItsOwn() => UiThread.Dispatch(async () =>
	{
		var mainWindow = new Window();
		var dialogService = CreateDialogService(mainWindow);

		var answer = dialogService.ShowConfirmationAsync("message");

		var messageBox = dialogService.OpenMessageBox!;
		messageBox.IsVisible.Should().BeTrue();
		messageBox.Owner.Should().BeNull();

		messageBox.Close();
		await answer;
	});

	[Fact]
	public Task Confirmation_WithVisibleMainWindow_IsOwnedByIt() => UiThread.Dispatch(async () =>
	{
		var mainWindow = new Window();
		mainWindow.Show();
		var dialogService = CreateDialogService(mainWindow);

		var answer = dialogService.ShowConfirmationAsync("message");

		var messageBox = dialogService.OpenMessageBox!;
		messageBox.Owner.Should().BeSameAs(mainWindow);

		messageBox.Close();
		await answer;
		mainWindow.Close();
	});

	[Fact]
	public Task Confirmation_FromWorkEntryDialog_IsOwnedByIt() => UiThread.Dispatch(async () =>
	{
		var dialogService = CreateDialogService();

		var entryDialogClosed = dialogService.ShowNewWorkEntryDialogAsync();
		var entryDialog = dialogService.OpenWorkEntryDialog!;

		var answer = dialogService.ShowConfirmationAsync("message");

		var messageBox = dialogService.OpenMessageBox;
		messageBox.Should().NotBeNull();
		messageBox!.Owner.Should().BeSameAs(entryDialog);

		messageBox.Close();
		await answer;

		entryDialog.Close();
		await entryDialogClosed;
	});
}
