using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

/// <summary>
/// Orchestrator for fetching and presenting work suggestions from external sources
/// </summary>
public interface IWorkSuggestionOrchestrator
{
	/// <summary>
	/// Whether any suggestion plugins are currently enabled
	/// </summary>
	bool HasSuggestionPlugins { get; }

	/// <summary>
	/// Gets suggestions grouped by plugin
	/// </summary>
	Task<IReadOnlyList<SuggestionGroup>> GetGroupedSuggestionsAsync(DateTime date, CancellationToken cancellationToken);

	/// <summary>
	/// Searches a specific plugin for suggestions matching a query.
	/// When query is empty, returns default suggestions (same as initial load).
	/// </summary>
	Task<IReadOnlyList<WorkSuggestionViewModel>> SearchPluginAsync(string pluginId, string query, DateTime date, CancellationToken cancellationToken);
}
