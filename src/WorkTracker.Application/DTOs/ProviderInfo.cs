using WorkTracker.Plugin.Abstractions;

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

	/// <summary>
	/// Submission modes advertised by the underlying plugin.
	/// Used by the UI to filter providers compatible with the selected mode.
	/// </summary>
	public WorklogSubmissionMode SupportedModes { get; init; } = WorklogSubmissionMode.Timed;

	public override string ToString() => Name;
}
