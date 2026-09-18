namespace WorkTracker.Application.Settings;

/// <summary>
/// Reads the plugin slice of the user settings file written by the GUI, so a host without the
/// settings UI (the CLI) can still start the plugins the user enabled and configured there.
/// Reading never throws: a missing or unreadable file yields empty settings.
/// </summary>
public interface IPluginSettingsReader
{
	Task<PluginSettings> ReadAsync(CancellationToken cancellationToken = default);
}
