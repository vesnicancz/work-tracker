namespace WorkTracker.Application.Plugins;

/// <summary>
/// Decides where the configuration of a plugin comes from, so that what the Settings dialog shows is what
/// the plugin started with.
/// </summary>
public interface IPluginConfigurationSource
{
	/// <summary>
	/// The configuration of each plugin by its id. A plugin saved in user settings gets exactly that;
	/// the application's configuration only provides the initial values of one that isn't there yet.
	/// </summary>
	Dictionary<string, Dictionary<string, string>> Resolve(IReadOnlyDictionary<string, Dictionary<string, string>> userConfigurations);
}
