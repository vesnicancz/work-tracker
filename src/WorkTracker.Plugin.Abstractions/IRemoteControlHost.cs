namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// The application as seen by an <see cref="IRemoteControlPlugin"/>. Members can be called from any
/// thread; <see cref="StateChanged"/> can be raised on any thread as well.
/// </summary>
public interface IRemoteControlHost
{
	/// <summary>
	/// Raised when anything in <see cref="RemoteControlState"/> changes, including when
	/// <see cref="RemoteControlState.ScheduledWork"/> begins or ends and when a new day begins. Running
	/// times are left out: consumers count them from the values in the state themselves.
	/// </summary>
	event EventHandler? StateChanged;

	/// <param name="lastWorkMaxAge">How long ago work may have started to be offered as <see cref="RemoteControlState.LastWork"/>.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<RemoteControlState> GetStateAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken);

	/// <summary>
	/// Starts tracking new work. Work that is already being tracked is stopped first.
	/// </summary>
	Task<PluginResult> StartWorkAsync(string? ticketId, string? description, CancellationToken cancellationToken);

	/// <summary>
	/// Stops the work being tracked. When nothing is, ends the <see cref="RemoteControlState.ScheduledWork"/>
	/// now, for a meeting that finished early.
	/// </summary>
	Task<PluginResult> StopWorkAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Starts tracking the most recent work again.
	/// </summary>
	/// <param name="lastWorkMaxAge">How long ago the work may have started. Fails when there is no such work.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<PluginResult> ResumeLastWorkAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken);

	/// <summary>
	/// Moves the start of the active work by <paramref name="delta"/>, for work that was started late
	/// or too early. Fails rather than touching another entry when the new start would overlap one.
	/// </summary>
	Task<PluginResult> ShiftActiveWorkStartAsync(TimeSpan delta, CancellationToken cancellationToken);

	/// <summary>
	/// Adds a work entry with a given time, as the application does for a suggestion with a time slot.
	/// Work that is being tracked and overlaps it ends at its start. Fails rather than touching any other
	/// entry: there is no way to ask the user about that here. Without an end, the new work keeps running.
	/// </summary>
	Task<PluginResult> AddWorkEntryAsync(string? ticketId, string? description, DateTimeOffset startTime, DateTimeOffset? endTime, CancellationToken cancellationToken);

	/// <summary>
	/// Gets today's suggestions from all enabled suggestion plugins, in the order of the plugins. A plugin
	/// that fails does not fail the call: it is listed in <see cref="RemoteSuggestions.FailedSources"/>.
	/// The results are shared with the application's suggestions dialog, including its short-lived cache.
	/// </summary>
	Task<RemoteSuggestions> GetSuggestionsAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Gets the time worked on <paramref name="date"/>, or in the week it belongs to, counted the same way
	/// as <see cref="RemoteControlState.WorkedTime"/>.
	/// </summary>
	Task<RemoteWorkedTimeSummary> GetWorkedTimeAsync(DateOnly date, RemoteWorkedTimePeriod period, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the entries that start between the start of <paramref name="from"/> and the end of
	/// <paramref name="to"/>, in order of their start.
	/// </summary>
	Task<IReadOnlyList<RemoteWorkEntry>> GetWorkEntriesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

	/// <summary>
	/// Replaces the ticket and the description of an entry, null clears it. The times stay as they are.
	/// Fails when neither would be left.
	/// </summary>
	Task<PluginResult<RemoteWorkEntry>> UpdateWorkEntryTextAsync(int id, string? ticketId, string? description, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the enabled worklog upload plugins.
	/// </summary>
	IReadOnlyList<RemoteWorklogProvider> GetWorklogProviders();

	/// <summary>
	/// Gets what <see cref="SubmitWorklogsAsync"/> would send for the same arguments, empty days included.
	/// </summary>
	/// <param name="date">The day, or any day of the week.</param>
	/// <param name="period">Whether it is the day or its whole week.</param>
	/// <param name="mode">A single mode, not a combination.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<PluginResult<IReadOnlyList<RemoteWorklogDay>>> PreviewWorklogsAsync(DateOnly date, RemoteWorkedTimePeriod period, WorklogSubmissionMode mode, CancellationToken cancellationToken);

	/// <summary>
	/// Submits the completed work of the day or the week of <paramref name="date"/>. Can't be taken back.
	/// Succeeds even when some worklogs were refused: see <see cref="RemoteSubmissionResult.Errors"/>.
	/// </summary>
	Task<PluginResult<RemoteSubmissionResult>> SubmitWorklogsAsync(DateOnly date, RemoteWorkedTimePeriod period, WorklogSubmissionMode mode, string providerId, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the user's favorites, in the order from the settings.
	/// </summary>
	IReadOnlyList<RemoteFavorite> GetFavorites();

	/// <summary>
	/// Opens the application's new work entry dialog with these values, or brings forward one that is already
	/// open. Completes once the dialog is up, without waiting for the user to close it.
	/// </summary>
	Task<PluginResult> ShowNewWorkEntryDialogAsync(string? ticketId, string? description, CancellationToken cancellationToken);

	Task<PluginResult> StartPomodoroAsync(CancellationToken cancellationToken);

	Task<PluginResult> StopPomodoroAsync(CancellationToken cancellationToken);

	Task<PluginResult> SkipPomodoroPhaseAsync(CancellationToken cancellationToken);
}
