using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Orchestrators;

public interface ISettingsOrchestrator
{
	List<PluginViewModel> LoadPlugins();

	/// <summary>
	/// Checks the configuration of every enabled plugin and returns one line per problem, prefixed
	/// with the plugin's name. Empty when everything is valid.
	/// </summary>
	Task<IReadOnlyList<string>> ValidatePluginConfigurationsAsync(IEnumerable<PluginViewModel> plugins, CancellationToken cancellationToken);

	Task SaveSettingsAsync(SettingsSaveRequest request, CancellationToken cancellationToken);

	Task<string> TestConnectionAsync(PluginViewModel plugin, IProgress<string>? progress, CancellationToken cancellationToken);
}
