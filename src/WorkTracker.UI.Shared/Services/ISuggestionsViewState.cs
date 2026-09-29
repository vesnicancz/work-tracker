using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Remembers which plugin group the user last had expanded in the Suggestions dialog, so the
/// next open restores that choice — within the session and across restarts.
/// </summary>
public interface ISuggestionsViewState
{
	string? LastExpandedPluginId { get; set; }
}

/// <summary>
/// Backs the expanded group by the settings file, so the choice survives a restart.
/// <para>
/// Reads go straight to the in-memory settings; a write is only persisted when the value actually
/// changes, and is fired off asynchronously because it happens on the UI thread while the user is
/// clicking through groups. A failed write costs nothing but the restored state.
/// </para>
/// </summary>
internal sealed class SuggestionsViewState : ISuggestionsViewState
{
	private readonly ISettingsService _settingsService;
	private readonly ILogger<SuggestionsViewState> _logger;

	public SuggestionsViewState(ISettingsService settingsService, ILogger<SuggestionsViewState> logger)
	{
		_settingsService = settingsService;
		_logger = logger;
	}

	public string? LastExpandedPluginId
	{
		get => _settingsService.Settings.LastExpandedSuggestionPluginId;
		set
		{
			var settings = _settingsService.Settings;
			if (settings.LastExpandedSuggestionPluginId == value)
			{
				return;
			}

			settings.LastExpandedSuggestionPluginId = value;
			_ = PersistAsync(settings);
		}
	}

	private async Task PersistAsync(ApplicationSettings settings)
	{
		try
		{
			await _settingsService.SaveSettingsAsync(settings);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to persist the expanded suggestions group");
		}
	}
}
