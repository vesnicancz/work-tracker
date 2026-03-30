using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public interface ISettingsOrchestrator
{
	List<PluginViewModel> LoadPlugins();

	Task SaveSettingsAsync(SettingsSaveRequest request, CancellationToken cancellationToken);

	Task<string> TestConnectionAsync(PluginViewModel plugin, IProgress<string>? progress, CancellationToken cancellationToken);
}