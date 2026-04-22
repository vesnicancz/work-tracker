namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Session-scoped state for the Suggestions dialog. Remembers which plugin
/// group the user last had expanded so the next open restores that choice.
/// </summary>
public interface ISuggestionsViewState
{
	string? LastExpandedPluginId { get; set; }
}

internal sealed class SuggestionsViewState : ISuggestionsViewState
{
	public string? LastExpandedPluginId { get; set; }
}
