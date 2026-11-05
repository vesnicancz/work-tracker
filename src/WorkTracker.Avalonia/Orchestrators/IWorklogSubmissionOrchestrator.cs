using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Orchestrators;

public interface IWorklogSubmissionOrchestrator
{
	List<ProviderInfo> LoadAvailableProviders();

	Task<IReadOnlyList<WorklogSubmissionDto>> LoadPreviewAsync(DateTime date, bool isWeekly, WorklogSubmissionMode mode, CancellationToken cancellationToken);

	Task<SubmissionOutcome> SubmitAsync(IReadOnlyList<WorklogDto> worklogs, string providerId, string providerName, WorklogSubmissionMode mode, CancellationToken cancellationToken);
}
