namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin interface for work suggestion providers that fetch potential work items
/// from external sources (calendars, issue trackers, etc.)
/// </summary>
public interface IWorkSuggestionPlugin : ITestablePlugin
{
	/// <summary>
	/// Gets work suggestions for a specific date
	/// </summary>
	/// <param name="date">The date to get suggestions for</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>List of work suggestions from the external source</returns>
	Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(DateTime date, CancellationToken cancellationToken);

	/// <summary>
	/// Whether this plugin supports text-based search (e.g., Jira issue search).
	/// When false, only date-based GetSuggestionsAsync is used.
	/// </summary>
	bool SupportsSearch { get; }

	/// <summary>
	/// Searches for suggestions matching a text query.
	/// Only called when <see cref="SupportsSearch"/> is true.
	/// </summary>
	Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(string query, CancellationToken cancellationToken);
}
