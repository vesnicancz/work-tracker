namespace WorkTracker.Application.DTOs;

/// <summary>
/// Represents information about a worklog upload provider (plugin)
/// </summary>
public class ProviderInfo
{
	/// <summary>
	/// Plugin ID (e.g., "com.worktracker.tempo")
	/// </summary>
	public required string Id { get; init; }

	/// <summary>
	/// Display name of the plugin (e.g., "Tempo")
	/// </summary>
	public required string Name { get; init; }

	public override string ToString() => Name;
}
