namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin metadata containing information about the plugin
/// </summary>
public class PluginMetadata
{
	/// <summary>
	/// Unique identifier for the plugin
	/// </summary>
	public required string Id { get; init; }

	/// <summary>
	/// Display name of the plugin
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Plugin version
	/// </summary>
	public required Version Version { get; init; }

	/// <summary>
	/// Plugin author
	/// </summary>
	public required string Author { get; init; }

	/// <summary>
	/// Plugin description
	/// </summary>
	public string? Description { get; init; }

	/// <summary>
	/// Plugin website or repository URL
	/// </summary>
	public string? Website { get; init; }

	/// <summary>
	/// Minimum WorkTracker version required
	/// </summary>
	public Version? MinimumAppVersion { get; init; }

	/// <summary>
	/// Plugin tags/categories
	/// </summary>
	public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
