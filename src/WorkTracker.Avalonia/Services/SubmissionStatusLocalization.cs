using WorkTracker.Application.Common;

namespace WorkTracker.Avalonia.Services;

public static class SubmissionStatusLocalization
{
	public static string FormatSubmissionStatus(this ILocalizationService localization, SubmissionResult submission, string providerName)
	{
		if (!submission.HasFailures)
		{
			return localization.GetFormattedString("SubmissionSuccess", submission.SuccessfulEntries, providerName);
		}

		if (submission.SuccessfulEntries == 0)
		{
			return localization.GetFormattedString("SubmissionAllFailed", submission.TotalEntries, providerName);
		}

		return localization.GetFormattedString("SubmissionPartial", submission.SuccessfulEntries, providerName, submission.FailureCount);
	}
}
