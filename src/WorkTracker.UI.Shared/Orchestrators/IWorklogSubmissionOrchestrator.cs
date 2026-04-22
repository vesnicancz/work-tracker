using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public interface IWorklogSubmissionOrchestrator
{
	List<ProviderInfo> LoadAvailableProviders();

	Task<PreviewLoadResult> LoadPreviewAsync(DateTime date, bool isWeekly, WorklogSubmissionMode mode, string noTicketLabel, CancellationToken cancellationToken);

	Task<SubmissionOutcome> SubmitAsync(IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName, WorklogSubmissionMode mode, CancellationToken cancellationToken);

	Task<SubmissionOutcome> RetryFailedAsync(IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName, WorklogSubmissionMode mode, CancellationToken cancellationToken);

	void ResetItems(IReadOnlyList<WorklogPreviewItem> items);

	void InvertSelection(IReadOnlyList<WorklogPreviewItem> items);

	void SelectAll(IReadOnlyList<WorklogPreviewItem> items);

	string FormatDuration(int seconds);
}