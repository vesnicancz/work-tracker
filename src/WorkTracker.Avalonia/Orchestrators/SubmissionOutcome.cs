using WorkTracker.Application.Common;

namespace WorkTracker.Avalonia.Orchestrators;

/// <summary>
/// What became of a submission. Without <paramref name="Delivered"/> the provider gave no answer about
/// individual worklogs, so what is known about them from before still holds.
/// </summary>
/// <param name="Delivered">Whether the provider took the submission and reported on it.</param>
/// <param name="StatusMessage">What to tell the user.</param>
/// <param name="Errors">The failures the provider reported, by position in the submitted list.</param>
public record SubmissionOutcome(bool Delivered, string StatusMessage, IReadOnlyList<SubmissionError> Errors);
