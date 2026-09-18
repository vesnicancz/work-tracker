using Avalonia.Controls;
using FluentAssertions;
using WorkTracker.Avalonia.Tests.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Tests.Smoke;

/// <summary>
/// The dialog lists every provider and disables the modes the selected one cannot do, so these
/// bindings — not a filtered dropdown — are what keeps an unsupported mode out of reach.
/// </summary>
public class SubmitWorklogDialogModeSmokeTests
{
	[Fact]
	public Task ModeRadioButtons_FollowWhatTheSelectedProviderSupports() => UiThread.Dispatch(() =>
	{
		var harness = new SubmitWorklogViewModelHarness();
		using var viewModel = harness.CreateViewModel();

		var dialog = new SubmitWorklogDialog { DataContext = viewModel };
		dialog.Show();

		try
		{
			var timed = dialog.FindControl<RadioButton>("TimedModeRadio")!;
			var aggregated = dialog.FindControl<RadioButton>("AggregatedModeRadio")!;
			var providers = dialog.FindControl<ComboBox>("ProviderComboBox")!;

			// Tempo is Timed-only: it stays listed, and Aggregated is disabled rather than hidden.
			viewModel.SelectedProvider.Should().Be(SubmitWorklogViewModelHarness.TimedOnlyProvider);
			providers.ItemCount.Should().Be(2);
			timed.IsEnabled.Should().BeTrue();
			timed.IsChecked.Should().BeTrue();
			aggregated.IsEnabled.Should().BeFalse();

			viewModel.SelectedProvider = SubmitWorklogViewModelHarness.BothModesProvider;

			aggregated.IsEnabled.Should().BeTrue();

			viewModel.SelectedMode = WorklogSubmissionMode.Aggregated;

			aggregated.IsChecked.Should().BeTrue();
			timed.IsChecked.Should().BeFalse();
			providers.ItemCount.Should().Be(2, "the provider list is never filtered by the mode");
		}
		finally
		{
			dialog.Close();
		}
	});
}
