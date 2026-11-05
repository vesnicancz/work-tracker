namespace WorkTracker.Avalonia.Orchestrators;

public interface ISettingsOrchestrator
{
	List<PluginSettingsEntry> LoadPlugins();

	/// <summary>
	/// Checks the configuration of every enabled plugin and returns one line per problem, prefixed
	/// with the plugin's name. Empty when everything is valid.
	/// </summary>
	Task<IReadOnlyList<string>> ValidatePluginConfigurationsAsync(IEnumerable<PluginSettingsEntry> plugins, CancellationToken cancellationToken);

	Task SaveSettingsAsync(SettingsSaveRequest request, CancellationToken cancellationToken);

	Task<string> TestConnectionAsync(PluginSettingsEntry plugin, IProgress<string>? progress, CancellationToken cancellationToken);
}
