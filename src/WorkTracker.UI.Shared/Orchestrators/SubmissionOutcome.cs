namespace WorkTracker.UI.Shared.Orchestrators;

public record SubmissionOutcome(bool AllSucceeded, bool HasFailedItems, string StatusMessage);