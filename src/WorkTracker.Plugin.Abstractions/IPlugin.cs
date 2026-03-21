namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base interface for all WorkTracker plugins
/// </summary>
public interface IPlugin
{
	/// <summary>
	/// Gets the plugin metadata
	/// </summary>
	PluginMetadata Metadata { get; }

	/// <summary>
	/// Initializes the plugin with configuration
	/// </summary>
	/// <param name="configuration">Plugin-specific configuration</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if initialization was successful</returns>
	Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Called when the plugin is being unloaded
	/// </summary>
	Task ShutdownAsync();

	/// <summary>
	/// Validates the plugin configuration
	/// </summary>
	/// <param name="configuration">Configuration to validate</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Validation result with any error messages</returns>
	Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken = default);
}
