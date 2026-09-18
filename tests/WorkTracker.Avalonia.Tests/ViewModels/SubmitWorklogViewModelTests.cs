using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.DTOs;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Tests.ViewModels;

public class SubmitWorklogViewModelTests
{
	private static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	private static readonly ProviderInfo TimedOnlyProvider = new()
	{
		Id = "tempo",
		Name = "Tempo",
		SupportedModes = WorklogSubmissionMode.Timed,
	};

	private static readonly ProviderInfo BothModesProvider = new()
	{
		Id = "goran",
		Name = "Goran",
		SupportedModes = WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated,
	};

	private sealed class Harness
	{
		public Mock<IWorklogSubmissionOrchestrator> Orchestrator { get; } = new();
		public Mock<ISettingsService> Settings { get; } = new();
		public ApplicationSettings SettingsModel { get; } = new();
		public List<WorklogPreviewItem> PreviewItems { get; } = new();

		public Harness()
		{
			Settings.SetupGet(s => s.Settings).Returns(SettingsModel);
			Orchestrator.Setup(o => o.LoadAvailableProviders())
				.Returns([TimedOnlyProvider, BothModesProvider]);
			Orchestrator.Setup(o => o.FormatDuration(It.IsAny<int>()))
				.Returns((int seconds) => $"{seconds}s");
			Orchestrator
				.Setup(o => o.LoadPreviewAsync(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new PreviewLoadResult(
					PreviewItems,
					PreviewItems.Where(i => !i.IsDateHeader).Sum(i => i.Duration),
					PreviewItems.Count(i => !i.IsDateHeader)));
		}

		public SubmitWorklogViewModel CreateViewModel()
		{
			var localization = new Mock<ILocalizationService>();
			localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
			localization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
				.Returns((string key, object[] _) => key);

			var timeProvider = new Mock<TimeProvider>();
			timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(LocalNow, TimeSpan.Zero));
			timeProvider.SetupGet(t => t.LocalTimeZone).Returns(TimeZoneInfo.Utc);

			return new SubmitWorklogViewModel(
				Orchestrator.Object,
				localization.Object,
				Settings.Object,
				timeProvider.Object,
				NullLogger<SubmitWorklogViewModel>.Instance);
		}
	}

	private static WorklogPreviewItem Item(int duration = 3600, bool selected = true) =>
		new()
		{
			Date = LocalNow.Date,
			TicketId = "PROJ-1",
			Duration = duration,
			IsSelected = selected,
		};

	[Fact]
	public void Constructor_NoRememberedProvider_RestoresGlobalModeAndAProviderThatSupportsIt()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.IsAggregatedMode.Should().BeTrue();
		vm.SelectedProvider.Should().Be(BothModesProvider, "Tempo cannot do Aggregated");
	}

	[Fact]
	public void Constructor_TimedMode_ListsAllProvidersAndSelectsFirst()
	{
		var harness = new Harness();

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
		vm.AvailableProviders.Should().HaveCount(2);
		vm.SelectedProvider.Should().Be(TimedOnlyProvider);
	}

	[Fact]
	public void Constructor_RestoresRememberedProviderAndItsOwnMode()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Timed;
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedProvider.Should().Be(BothModesProvider);
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated, "the mode is remembered per provider");
	}

	[Fact]
	public void Constructor_UnknownRememberedProvider_FallsBackToGlobalMode()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;
		harness.SettingsModel.LastSubmissionProviderId = "uninstalled-plugin";

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.SelectedProvider.Should().Be(BothModesProvider);
	}

	[Fact]
	public void Constructor_RememberedModeNoLongerSupported_FallsBackToASupportedMode()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;
		harness.SettingsModel.LastSubmissionProviderId = "tempo";
		harness.SettingsModel.SubmissionModeByProvider["tempo"] = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedProvider.Should().Be(TimedOnlyProvider);
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed, "Tempo only supports Timed");
	}

	[Fact]
	public void Constructor_DoesNotPersistTheRestoredSelection()
	{
		var harness = new Harness();

		using var vm = harness.CreateViewModel();

		harness.SettingsModel.LastSubmissionProviderId.Should().BeNull();
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public void AvailableProviders_AreNeverFilteredByTheSelectedMode()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		// Tempo cannot do Aggregated, but it stays listed — the provider is the primary choice and
		// hiding it is what would make switching back to it impossible.
		vm.AvailableProviders.Should().HaveCount(2);
		vm.SelectedProvider.Should().Be(BothModesProvider);
	}

	[Fact]
	public void CanUseMode_FollowsWhatTheSelectedProviderSupports()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.SelectedProvider.Should().Be(TimedOnlyProvider);
		vm.CanUseTimedMode.Should().BeTrue();
		vm.CanUseAggregatedMode.Should().BeFalse();

		vm.SelectedProvider = BothModesProvider;

		vm.CanUseTimedMode.Should().BeTrue();
		vm.CanUseAggregatedMode.Should().BeTrue();
	}

	[Fact]
	public void ModeChange_UnsupportedByTheSelectedProvider_IsIgnored()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider.Should().Be(TimedOnlyProvider);

		// The dialog disables this radio button; a programmatic set must not slip past it either.
		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
		vm.IsTimedMode.Should().BeTrue();
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public void ModeChange_RemembersTheModeForTheSelectedProvider()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		harness.SettingsModel.LastSubmissionMode.Should().Be(WorklogSubmissionMode.Aggregated);
		harness.SettingsModel.LastSubmissionProviderId.Should().Be("goran");
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should().Be(WorklogSubmissionMode.Aggregated);
	}

	[Fact]
	public void ProviderChange_RestoresTheModeRememberedForThatProvider()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionProviderId = "tempo";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		using var vm = harness.CreateViewModel();
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);

		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.SelectedProvider.Should().Be(BothModesProvider);
	}

	[Fact]
	public void ProviderChange_ToAProviderThatCannotDoTheCurrentMode_SwitchesTheMode()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		using var vm = harness.CreateViewModel();
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);

		vm.SelectedProvider = TimedOnlyProvider;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should()
			.Be(WorklogSubmissionMode.Aggregated, "leaving Goran must not rewrite its mode");
	}

	[Fact]
	public void ProviderChange_WithoutRememberedMode_KeepsCurrentMode()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);
	}

	[Fact]
	public void ProviderChange_PersistsTheProviderAndItsMode()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.SelectedProvider = BothModesProvider;

		harness.SettingsModel.LastSubmissionProviderId.Should().Be("goran");
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should().Be(WorklogSubmissionMode.Timed);
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(harness.SettingsModel, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Send_RemembersTheProviderAndModeItWasSentWith()
	{
		var harness = new Harness();
		harness.PreviewItems.Add(Item());
		harness.SettingsModel.LastSubmissionProviderId = "goran";
		harness.SettingsModel.SubmissionModeByProvider["goran"] = WorklogSubmissionMode.Aggregated;
		harness.Orchestrator
			.Setup(o => o.SubmitAsync(It.IsAny<IReadOnlyList<WorklogPreviewItem>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new SubmissionOutcome(AllSucceeded: true, HasFailedItems: false, StatusMessage: "sent"));
		using var vm = harness.CreateViewModel();
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);

		// Nothing in the dialog is touched, so only the submit writes the selection back.
		await vm.SendCommand.ExecuteAsync(null);

		harness.SettingsModel.LastSubmissionProviderId.Should().Be("goran");
		harness.SettingsModel.SubmissionModeByProvider["goran"].Should().Be(WorklogSubmissionMode.Aggregated);
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(harness.SettingsModel, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void SwitchingProvidersBackAndForth_KeepsEachProvidersOwnMode()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		// Goran is set to Aggregated...
		vm.SelectedProvider = BothModesProvider;
		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		// ...and picking Tempo, which is Timed-only, switches the mode without touching Goran's.
		vm.SelectedProvider = TimedOnlyProvider;
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Timed);

		// Back to Goran: Aggregated returns.
		vm.SelectedProvider = BothModesProvider;
		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
	}

	[Fact]
	public async Task InitializeAsync_LoadsPreviewAndTotals()
	{
		var harness = new Harness();
		harness.PreviewItems.Add(Item(duration: 3600));
		harness.PreviewItems.Add(Item(duration: 1800));
		using var vm = harness.CreateViewModel();

		await vm.InitializeAsync(LocalNow.Date, isWeek: false);

		vm.PreviewItems.Should().HaveCount(2);
		vm.TotalTimeDisplay.Should().Be("5400s");
		vm.StatusMessage.Should().Be("ReadyToSubmit");
		vm.IsLoading.Should().BeFalse();
	}

	[Fact]
	public async Task CanSend_RequiresSelectedItemAndProvider()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		await vm.InitializeAsync(LocalNow.Date, isWeek: false);
		vm.SendCommand.CanExecute(null).Should().BeFalse("there are no preview items");

		harness.PreviewItems.Add(Item());
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);
		vm.SendCommand.CanExecute(null).Should().BeTrue();

		vm.SelectedProvider = null;
		vm.SendCommand.CanExecute(null).Should().BeFalse("no provider is selected");
	}

	[Fact]
	public async Task Send_AllSucceeded_SetsDialogResult()
	{
		var harness = new Harness();
		harness.PreviewItems.Add(Item());
		harness.Orchestrator
			.Setup(o => o.SubmitAsync(It.IsAny<IReadOnlyList<WorklogPreviewItem>>(), "tempo", "Tempo", WorklogSubmissionMode.Timed, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new SubmissionOutcome(AllSucceeded: true, HasFailedItems: false, StatusMessage: "sent"));
		using var vm = harness.CreateViewModel();
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);

		await vm.SendCommand.ExecuteAsync(null);

		vm.DialogResult.Should().BeTrue();
		vm.HasFailedItems.Should().BeFalse();
		vm.StatusMessage.Should().Be("sent");
		vm.IsSending.Should().BeFalse();
	}

	[Fact]
	public async Task Send_WithFailures_EnablesRetry()
	{
		var harness = new Harness();
		var item = Item();
		harness.PreviewItems.Add(item);
		harness.Orchestrator
			.Setup(o => o.SubmitAsync(It.IsAny<IReadOnlyList<WorklogPreviewItem>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorklogSubmissionMode>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new SubmissionOutcome(AllSucceeded: false, HasFailedItems: true, StatusMessage: "1 failed"));
		using var vm = harness.CreateViewModel();
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);

		await vm.SendCommand.ExecuteAsync(null);
		item.HasError = true;

		vm.DialogResult.Should().BeFalse();
		vm.HasFailedItems.Should().BeTrue();
		vm.StatusMessage.Should().Be("1 failed");
		vm.RetryFailedCommand.CanExecute(null).Should().BeTrue();
	}

	[Fact]
	public async Task ItemDeselection_RecalculatesTotals()
	{
		var harness = new Harness();
		var first = Item(duration: 3600);
		var second = Item(duration: 1800);
		harness.PreviewItems.Add(first);
		harness.PreviewItems.Add(second);
		using var vm = harness.CreateViewModel();
		await vm.InitializeAsync(LocalNow.Date, isWeek: false);

		second.IsSelected = false;

		vm.TotalTimeDisplay.Should().Be("3600s");
	}
}
