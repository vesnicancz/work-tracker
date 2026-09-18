namespace WorkTracker.Application.Settings;

/// <summary>
/// The plugin-related slice of the persisted settings file.
/// <para>
/// Every host needs these two values to bring plugins up, but only the GUI owns the full settings
/// model. <c>ApplicationSettings</c> (UI.Shared) derives from this type rather than redeclaring the
/// properties, so the JSON names can never drift apart, and the CLI reads just this slice through
/// <see cref="IPluginSettingsReader"/> without taking a dependency on the UI layer.
/// </para>
/// </summary>
public class PluginSettings
{
	/// <summary>
	/// Plugin configurations (pluginId -> configuration dictionary).
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> PluginConfigurations { get; set; } = new();

	/// <summary>
	/// Enabled plugins (pluginId -> enabled state). Plugins absent from this map are disabled.
	/// </summary>
	public Dictionary<string, bool> EnabledPlugins { get; set; } = new();
}
