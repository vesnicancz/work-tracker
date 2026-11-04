namespace WorkTracker.Avalonia.Orchestrators;

public record SubmissionOutcome(bool AllSucceeded, bool HasFailedItems, string StatusMessage);
