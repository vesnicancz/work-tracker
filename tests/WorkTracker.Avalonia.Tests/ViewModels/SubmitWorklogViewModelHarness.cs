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

/// <summary>
/// Builds a <see cref="SubmitWorklogViewModel"/> over mocked collaborators. Shared with the
/// headless dialog smoke tests, which bind the real view to it.
/// </summary>
public sealed class SubmitWorklogViewModelHarness
{
	public static readonly DateTime LocalNow = new(2026, 1, 15, 12, 0, 0);

	public static readonly ProviderInfo TimedOnlyProvider = new()
	{
		Id = "tempo",
		Name = "Tempo",
		SupportedModes = WorklogSubmissionMode.Timed,
	};

	public static readonly ProviderInfo BothModesProvider = new()
	{
		Id = "goran",
		Name = "Goran",
		SupportedModes = WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated,
	};

	public Mock<IWorklogSubmissionOrchestrator> Orchestrator { get; } = new();
	public Mock<ISettingsService> Settings { get; } = new();
	public ApplicationSettings SettingsModel { get; } = new();
	public List<WorklogPreviewItem> PreviewItems { get; } = new();

	public SubmitWorklogViewModelHarness()
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
