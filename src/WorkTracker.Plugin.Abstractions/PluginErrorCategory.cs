namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Categorizes plugin errors for structured failure handling
/// </summary>
public enum PluginErrorCategory
{
	Internal,
	Validation,
	Network,
	Authentication,
	NotFound
}
