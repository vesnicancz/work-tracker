namespace WorkTracker.Plugin.Abstractions;

/// <param name="Id">Identifies the entry for as long as it exists.</param>
/// <param name="TicketId">The ticket worked on.</param>
/// <param name="Description">The description of the work.</param>
/// <param name="StartTime">When the work started.</param>
/// <param name="EndTime">Null while the work is being tracked.</param>
public sealed record RemoteWorkEntry(int Id, string? TicketId, string? Description, DateTimeOffset StartTime, DateTimeOffset? EndTime);

/// <summary>
/// An enabled worklog upload plugin.
/// </summary>
public sealed record RemoteWorklogProvider(string Id, string Name, WorklogSubmissionMode SupportedModes);

public sealed record RemoteWorklogDay(DateOnly Date, IReadOnlyList<RemoteWorklog> Worklogs);

/// <param name="TicketId">The ticket worked on.</param>
/// <param name="Description">The description of the work.</param>
/// <param name="StartTime">In aggregated mode the earliest start of the group, not a real interval.</param>
/// <param name="DurationMinutes">The time worked.</param>
public sealed record RemoteWorklog(string? TicketId, string? Description, DateTimeOffset StartTime, int DurationMinutes);

/// <param name="Total">How many worklogs there were to submit.</param>
/// <param name="Successful">How many were accepted.</param>
/// <param name="Failed">How many were not.</param>
/// <param name="Errors">What went wrong. Can be there with no failed worklog, such as a timesheet that could not be closed.</param>
public sealed record RemoteSubmissionResult(int Total, int Successful, int Failed, IReadOnlyList<RemoteSubmissionError> Errors);

/// <param name="WorklogIndex">The position among the worklogs of the preview, counted across its days. Null when unknown.</param>
/// <param name="Message">What went wrong.</param>
public sealed record RemoteSubmissionError(int? WorklogIndex, string Message);
