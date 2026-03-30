namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin that supports connection testing.
/// </summary>
public interface ITestablePlugin : IPlugin
{
	Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Tests connection with progress reporting (e.g., for OAuth flows that require user action).
	/// </summary>
	Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken);
}
