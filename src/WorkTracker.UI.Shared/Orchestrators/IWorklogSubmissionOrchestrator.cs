using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public interface IWorklogSubmissionOrchestrator
{
	List<ProviderInfo> LoadAvailableProviders();

	Task<PreviewLoadResult> LoadPreviewAsync(DateTime date, bool isWeekly, string noTicketLabel);

	Task<SubmissionOutcome> SubmitAsync(IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName);

	Task<SubmissionOutcome> RetryFailedAsync(IReadOnlyList<WorklogPreviewItem> failedItems, string providerId, string providerName);

	bool MarkFailedItems(IReadOnlyList<WorklogPreviewItem> items, SubmissionResult submission);

	void ResetItems(IReadOnlyList<WorklogPreviewItem> items);

	string FormatDuration(int seconds);

	string FormatSubmissionStatus(SubmissionResult submission, string providerName);
}