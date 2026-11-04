namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// The application as seen by an <see cref="IRemoteControlPlugin"/>. Members can be called from any
/// thread; <see cref="StateChanged"/> can be raised on any thread as well.
/// </summary>
public interface IRemoteControlHost
{
	/// <summary>
	/// Raised when anything in <see cref="RemoteControlState"/> changes, except for the passing of
	/// time: consumers count down from the values in the state themselves.
	/// </summary>
	event EventHandler? StateChanged;

	/// <param name="lastWorkMaxAge">How long ago work may have started to be offered as <see cref="RemoteControlState.LastWork"/>.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<RemoteControlState> GetStateAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken);

	/// <summary>
	/// Starts tracking new work. Work that is already being tracked is stopped first.
	/// </summary>
	Task<PluginResult> StartWorkAsync(string? ticketId, string? description, CancellationToken cancellationToken);

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

	Task<PluginResult> StartPomodoroAsync(CancellationToken cancellationToken);

	Task<PluginResult> StopPomodoroAsync(CancellationToken cancellationToken);

	Task<PluginResult> SkipPomodoroPhaseAsync(CancellationToken cancellationToken);
}
