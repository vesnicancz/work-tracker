namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// What an <see cref="IRemoteControlPlugin"/> can show about the application.
/// </summary>
/// <param name="ActiveWork">The work being tracked, or <c>null</c> when nothing is.</param>
/// <param name="LastWork">
/// The most recent work, offered for resuming. <c>null</c> while work is being tracked and when there
/// is none within the age limit the plugin asked for.
/// </param>
/// <param name="Pomodoro">The Pomodoro timer.</param>
public sealed record RemoteControlState(RemoteWorkItem? ActiveWork, RemoteWorkItem? LastWork, RemotePomodoroState Pomodoro);

/// <summary>
/// A work entry. At least one of <paramref name="TicketId"/> and <paramref name="Description"/> is set.
/// </summary>
public sealed record RemoteWorkItem(string? TicketId, string? Description, DateTimeOffset StartedAt);

/// <param name="Items">The suggestions of all plugins that answered.</param>
/// <param name="FailedSources">The names of the plugins that could not provide their suggestions.</param>
public sealed record RemoteSuggestions(IReadOnlyList<RemoteSuggestion> Items, IReadOnlyList<string> FailedSources);

/// <summary>
/// A suggested work item. Times are set for sources with a time slot, such as a calendar.
/// </summary>
public sealed record RemoteSuggestion(string Title, string? TicketId, DateTimeOffset? StartTime, DateTimeOffset? EndTime, string Source);

/// <summary>
/// A work item the user keeps at hand.
/// </summary>
/// <param name="Name">What to show for it.</param>
/// <param name="TicketId">The ticket to work on.</param>
/// <param name="Description">The description of the work.</param>
/// <param name="ShowAsTemplate">Whether it fills in the new entry dialog rather than starting work right away.</param>
public sealed record RemoteFavorite(string Name, string? TicketId, string? Description, bool ShowAsTemplate);

/// <summary>
/// The Pomodoro timer. <paramref name="TimeRemaining"/> is the value at the moment the state was read.
/// </summary>
public sealed record RemotePomodoroState(
	StatusIndicatorState Phase,
	TimeSpan TimeRemaining,
	int CompletedPomodoros,
	int PomodorosBeforeLongBreak);
