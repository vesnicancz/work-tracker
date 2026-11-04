using Avalonia.Controls;
using FluentAssertions;
using Moq;
using WorkTracker.Avalonia.Tests.ViewModels;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// After a failure the prominent button at the far right has to be the one that sends only the failed
/// rows, because the other one sends the finished rows again and creates duplicates. Moving on to
/// other data has to bring back the plain submit button.
/// </summary>
public class SubmitWorklogDialogButtonsSmokeTests
{
	private static (SubmitWorklogViewModel ViewModel, SubmitWorklogDialog Dialog, Button Send, Button Retry) CreateDialog()
	{
		var harness = new SubmitWorklogViewModelHarness();
		harness.PreviewItems.Add(new WorklogPreviewItem
		{
			Date = SubmitWorklogViewModelHarness.LocalNow.Date,
			TicketId = "PROJ-1",
			Duration = 3600,
			IsSelected = true,
		});
		harness.Orchestrator
			.Setup(o => o.SubmitAsync(It.IsAny<IReadOnlyList<WorklogPreviewItem>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new SubmissionOutcome(AllSucceeded: false, HasFailedItems: true, StatusMessage: "1 failed"));

		var viewModel = harness.CreateViewModel();
		var dialog = new SubmitWorklogDialog { DataContext = viewModel };
		dialog.Show();

		return (viewModel, dialog, dialog.FindControl<Button>("SendButton")!, dialog.FindControl<Button>("RetryButton")!);
	}

	private static void ShouldOfferPlainSubmit(SubmitWorklogViewModel viewModel, Button send, Button retry)
	{
		retry.IsVisible.Should().BeFalse();
		send.Classes.Should().Contain("accent");
		send.Classes.Should().NotContain("secondaryAction");
		viewModel.SubmitLabel.Should().Be("Submit");
		viewModel.SubmitToolTip.Should().BeNull();
	}

	private static void ShouldPreferRetry(Button send, Button retry)
	{
		retry.IsVisible.Should().BeTrue();
		send.Classes.Should().NotContain("accent");
		send.Classes.Should().Contain("secondaryAction");
	}

	[Fact]
	public Task RetryButton_SitsAtTheFarRightOfTheActions() => UiThread.Dispatch(() =>
	{
		var (viewModel, dialog, send, retry) = CreateDialog();

		try
		{
			var actions = ((StackPanel)send.Parent!).Children;
			actions[^1].Should().BeSameAs(retry);
			actions[^2].Should().BeSameAs(send);
		}
		finally
		{
			dialog.Close();
			viewModel.Dispose();
		}
	});

	[Fact]
	public Task AfterFailure_RetryIsProminent_AndAnotherDayBringsBackPlainSubmit() => UiThread.Dispatch(async () =>
	{
		var (viewModel, dialog, send, retry) = CreateDialog();

		try
		{
			await viewModel.InitializeAsync(SubmitWorklogViewModelHarness.LocalNow.Date, isWeek: false);
			ShouldOfferPlainSubmit(viewModel, send, retry);

			await viewModel.SendCommand.ExecuteAsync(null);
			ShouldPreferRetry(send, retry);

			viewModel.SelectedDate = SubmitWorklogViewModelHarness.LocalNow.Date.AddDays(-1);
			ShouldOfferPlainSubmit(viewModel, send, retry);
		}
		finally
		{
			dialog.Close();
			viewModel.Dispose();
		}
	});

	[Fact]
	public Task AfterFailure_SwitchingToTheWholeWeekBringsBackPlainSubmit() => UiThread.Dispatch(async () =>
	{
		var (viewModel, dialog, send, retry) = CreateDialog();

		try
		{
			await viewModel.InitializeAsync(SubmitWorklogViewModelHarness.LocalNow.Date, isWeek: false);
			await viewModel.SendCommand.ExecuteAsync(null);

			viewModel.IsWeekly = true;

			ShouldOfferPlainSubmit(viewModel, send, retry);
		}
		finally
		{
			dialog.Close();
			viewModel.Dispose();
		}
	});

	[Fact]
	public Task AfterFailure_ChangingTheModeBringsBackPlainSubmit() => UiThread.Dispatch(async () =>
	{
		var (viewModel, dialog, send, retry) = CreateDialog();

		try
		{
			await viewModel.InitializeAsync(SubmitWorklogViewModelHarness.LocalNow.Date, isWeek: false);
			viewModel.SelectedProvider = SubmitWorklogViewModelHarness.BothModesProvider;
			await viewModel.SendCommand.ExecuteAsync(null);
			ShouldPreferRetry(send, retry);

			viewModel.SelectedMode = WorklogSubmissionMode.Aggregated;

			ShouldOfferPlainSubmit(viewModel, send, retry);
		}
		finally
		{
			dialog.Close();
			viewModel.Dispose();
		}
	});

	[Fact]
	public Task AfterFailure_ResetBringsBackPlainSubmit() => UiThread.Dispatch(async () =>
	{
		var (viewModel, dialog, send, retry) = CreateDialog();

		try
		{
			await viewModel.InitializeAsync(SubmitWorklogViewModelHarness.LocalNow.Date, isWeek: false);
			await viewModel.SendCommand.ExecuteAsync(null);

			viewModel.ResetCommand.Execute(null);

			ShouldOfferPlainSubmit(viewModel, send, retry);
		}
		finally
		{
			dialog.Close();
			viewModel.Dispose();
		}
	});
}
