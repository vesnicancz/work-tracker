namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// Represents a group of suggestions from a single plugin
/// </summary>
public class SuggestionGroup
{
	public required string PluginId { get; init; }
	public required string PluginName { get; init; }
	public bool SupportsSearch { get; init; }
	public required IReadOnlyList<WorkSuggestionViewModel> Items { get; init; }
	public string? Error { get; init; }

	/// <summary>
	/// Icon name from plugin metadata (maps to MaterialIcon Kind).
	/// </summary>
	public string? IconHint { get; init; }
}
