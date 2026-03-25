namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Result of plugin configuration validation
/// </summary>
public class PluginValidationResult
{
	public bool IsValid { get; init; }
	public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

	public static PluginValidationResult Success() => new() { IsValid = true };

	public static PluginValidationResult Failure(params string[] errors) =>
		new() { IsValid = false, Errors = errors };
}
