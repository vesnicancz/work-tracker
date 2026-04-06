using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for work suggestion plugins. Inherits configuration, validation, and lifecycle from <see cref="PluginBase"/>.
/// </summary>
public abstract class WorkSuggestionPluginBase(ILogger logger) : PluginBase(logger), IWorkSuggestionPlugin
{
	public abstract Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken);

	public abstract Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(DateTime date, CancellationToken cancellationToken);

	public virtual bool SupportsSearch => false;

	public virtual Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(string query, CancellationToken cancellationToken)
	{
		return Task.FromResult(PluginResult<IReadOnlyList<WorkSuggestion>>.Failure("Search is not supported by this plugin"));
	}
}
