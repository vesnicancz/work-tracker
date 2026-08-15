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
	public void Constructor_RestoresPersistedSubmissionMode()
	{
		var harness = new Harness();
		harness.SettingsModel.LastSubmissionMode = WorklogSubmissionMode.Aggregated;

		using var vm = harness.CreateViewModel();

		vm.SelectedMode.Should().Be(WorklogSubmissionMode.Aggregated);
		vm.IsAggregatedMode.Should().BeTrue();
		vm.AvailableProviders.Should().ContainSingle(p => p.Id == "goran");
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
	public void ModeChange_RefiltersProvidersAndPersistsSetting()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();

		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		vm.AvailableProviders.Should().ContainSingle(p => p.Id == "goran");
		vm.SelectedProvider.Should().Be(BothModesProvider);
		harness.SettingsModel.LastSubmissionMode.Should().Be(WorklogSubmissionMode.Aggregated);
		harness.Settings.Verify(
			s => s.SaveSettingsAsync(harness.SettingsModel, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public void ModeChange_KeepsSelectedProviderWhenStillCompatible()
	{
		var harness = new Harness();
		using var vm = harness.CreateViewModel();
		vm.SelectedProvider = BothModesProvider;

		vm.SelectedMode = WorklogSubmissionMode.Aggregated;

		vm.SelectedProvider.Should().Be(BothModesProvider);
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
