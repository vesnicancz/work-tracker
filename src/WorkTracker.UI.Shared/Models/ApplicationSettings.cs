namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Application settings model
/// </summary>
public class ApplicationSettings
{
	/// <summary>
	/// Behavior when closing the main window
	/// </summary>
	public CloseWindowBehavior CloseWindowBehavior { get; set; } = CloseWindowBehavior.MinimizeToTray;

	/// <summary>
	/// Whether the application should start automatically with Windows
	/// </summary>
	public bool StartWithWindows { get; set; }

	/// <summary>
	/// Whether the application should start minimized to tray
	/// </summary>
	public bool StartMinimized { get; set; }

	/// <summary>
	/// Plugin configurations (pluginId -> configuration dictionary)
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> PluginConfigurations { get; set; } = new();

	/// <summary>
	/// Enabled plugins (pluginId -> enabled state)
	/// </summary>
	public Dictionary<string, bool> EnabledPlugins { get; set; } = new();

	/// <summary>
	/// Favorite work items for quick access from tray menu
	/// </summary>
	public List<FavoriteWorkItem> FavoriteWorkItems { get; set; } = new();

	/// <summary>
	/// Application theme ("Dark" or "Light")
	/// </summary>
	public string Theme { get; set; } = "Modern Blue";
}
