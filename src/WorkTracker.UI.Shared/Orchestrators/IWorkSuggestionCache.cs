namespace WorkTracker.UI.Shared.Orchestrators;

/// <summary>
/// Invalidates the cached work suggestions, forcing the next call to refetch from plugins.
/// </summary>
public interface IWorkSuggestionCache
{
	void Invalidate();
}
