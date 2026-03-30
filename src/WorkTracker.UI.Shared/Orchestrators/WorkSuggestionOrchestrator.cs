using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

/// <summary>
/// Orchestrator for fetching and presenting work suggestions from external sources
/// </summary>
public class WorkSuggestionOrchestrator : IWorkSuggestionOrchestrator
{
	private readonly IPluginManager _pluginManager;
	private readonly ILogger<WorkSuggestionOrchestrator> _logger;

	public WorkSuggestionOrchestrator(IPluginManager pluginManager, ILogger<WorkSuggestionOrchestrator> logger)
	{
		_pluginManager = pluginManager;
		_logger = logger;
	}

	public bool HasSuggestionPlugins => _pluginManager.WorkSuggestionPlugins.Any();

	public async Task<IReadOnlyList<SuggestionGroup>> GetGroupedSuggestionsAsync(DateTime date, CancellationToken cancellationToken)
	{
		var plugins = _pluginManager.WorkSuggestionPlugins.ToList();
		if (plugins.Count == 0)
		{
			return [];
		}

		var tasks = plugins.Select(async plugin =>
		{
			try
			{
				var result = await plugin.GetSuggestionsAsync(date, cancellationToken);
				if (result.IsFailure)
				{
					_logger.LogWarning("Plugin {Name} failed: {Error}", plugin.Metadata.Name, result.Error);
					return new SuggestionGroup
					{
						PluginId = plugin.Metadata.Id,
						PluginName = plugin.Metadata.Name,
						SupportsSearch = plugin.SupportsSearch,
						Items = [],
						Error = result.Error,
						IconHint = plugin.Metadata.IconName ?? "LightbulbOutline"
					};
				}

				var items = (result.Value ?? [])
					.Select(MapToViewModel)
					.OrderBy(s => s.StartTime ?? DateTime.MaxValue)
					.ThenBy(s => s.Title)
					.ToList();

				return new SuggestionGroup
				{
					PluginId = plugin.Metadata.Id,
					PluginName = plugin.Metadata.Name,
					SupportsSearch = plugin.SupportsSearch,
					Items = items,
					IconHint = plugin.Metadata.IconName ?? "LightbulbOutline"
				};
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				_logger.LogError(ex, "Plugin {Name} threw an exception: {Message}", plugin.Metadata.Name, ex.Message);
				return new SuggestionGroup
				{
					PluginId = plugin.Metadata.Id,
					PluginName = plugin.Metadata.Name,
					SupportsSearch = plugin.SupportsSearch,
					Items = [],
					Error = "Failed to load suggestions",
					IconHint = plugin.Metadata.IconName ?? "LightbulbOutline"
				};
			}
		});

		var groups = await Task.WhenAll(tasks);
		return groups;
	}

	public async Task<IReadOnlyList<WorkSuggestionViewModel>> SearchPluginAsync(
		string pluginId, string query, DateTime date, CancellationToken cancellationToken)
	{
		var plugin = _pluginManager.GetPlugin<IWorkSuggestionPlugin>(pluginId);
		if (plugin == null)
		{
			return [];
		}

		try
		{
			var result = string.IsNullOrWhiteSpace(query) || !plugin.SupportsSearch
				? await plugin.GetSuggestionsAsync(date, cancellationToken)
				: await plugin.SearchAsync(query, cancellationToken);

			if (result.IsFailure)
			{
				_logger.LogWarning("Search in plugin {Name} failed: {Error}", plugin.Metadata.Name, result.Error);
				return [];
			}

			return (result.Value ?? [])
				.Select(MapToViewModel)
				.OrderBy(s => s.StartTime ?? DateTime.MaxValue)
				.ThenBy(s => s.Title)
				.ToList();
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Search in plugin {Name} threw an exception", plugin.Metadata.Name);
			return [];
		}
	}

	private static WorkSuggestionViewModel MapToViewModel(WorkSuggestion s) => new()
	{
		Title = s.Title,
		TicketId = s.TicketId,
		Description = s.Description,
		StartTime = s.StartTime,
		EndTime = s.EndTime,
		Source = s.Source,
		SourceId = s.SourceId,
		SourceUrl = s.SourceUrl
	};

}
