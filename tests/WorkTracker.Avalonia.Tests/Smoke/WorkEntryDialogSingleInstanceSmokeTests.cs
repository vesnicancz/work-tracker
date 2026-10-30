using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// The global hotkey and the tray menu stay usable while a work entry dialog is open, so a second
/// request must bring the open dialog forward rather than stack another one on top of it.
/// </summary>
public class WorkEntryDialogSingleInstanceSmokeTests
{
	private static DialogService CreateDialogService()
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
			NullLogger<DialogService>.Instance);
	}

	[Fact]
	public Task SecondNewEntryRequest_WhileDialogOpen_ReusesIt() => UiThread.Dispatch(async () =>
	{
		var dialogService = CreateDialogService();

		var first = dialogService.ShowNewWorkEntryDialogAsync();
		var openDialog = dialogService.OpenWorkEntryDialog;
		openDialog.Should().NotBeNull();

		try
		{
			var second = dialogService.ShowNewWorkEntryDialogAsync();

			second.IsCompleted.Should().BeTrue();
			(await second).Should().BeFalse();
			first.IsCompleted.Should().BeFalse();
			dialogService.OpenWorkEntryDialog.Should().BeSameAs(openDialog);
		}
		finally
		{
			openDialog!.Close();
		}

		(await first).Should().BeFalse();
	});

	[Fact]
	public Task NewEntryRequest_AfterDialogClosed_OpensNewOne() => UiThread.Dispatch(async () =>
	{
		var dialogService = CreateDialogService();

		var first = dialogService.ShowNewWorkEntryDialogAsync();
		var firstDialog = dialogService.OpenWorkEntryDialog!;
		firstDialog.Close();
		await first;

		dialogService.OpenWorkEntryDialog.Should().BeNull();

		var second = dialogService.ShowNewWorkEntryDialogAsync();
		var secondDialog = dialogService.OpenWorkEntryDialog;
		try
		{
			secondDialog.Should().NotBeNull().And.NotBeSameAs(firstDialog);
		}
		finally
		{
			secondDialog?.Close();
		}

		await second;
	});
}
