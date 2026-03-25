using WorkTracker.Application.DTOs;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public interface IWorklogSubmissionOrchestrator
{
	List<ProviderInfo> LoadAvailableProviders();

	Task<PreviewLoadResult> LoadPreviewAsync(DateTime date, bool isWeekly, string noTicketLabel);

	Task<SubmissionOutcome> SubmitAsync(IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName);

	Task<SubmissionOutcome> RetryFailedAsync(IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName);

	void ResetItems(IReadOnlyList<WorklogPreviewItem> items);

	string FormatDuration(int seconds);
}