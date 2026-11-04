namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// What can be asked of a plugin without starting it: what it is and what it needs to run.
/// </summary>
public interface IPluginDescriptor
{
	/// <summary>
	/// Gets the plugin metadata
	/// </summary>
	PluginMetadata Metadata { get; }

	/// <summary>
	/// Gets the configuration fields for this plugin (e.g. API keys, colors, URLs).
	/// </summary>
	IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

	/// <summary>
	/// Validates the plugin configuration
	/// </summary>
	/// <param name="configuration">Configuration to validate</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Validation result with any error messages</returns>
	Task<PluginValidationResult> ValidateConfigurationAsync(IDictionary<string, string> configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base interface for all WorkTracker plugins. An instance runs on one configuration for its whole life:
/// the application initializes it once, and a new configuration means a new instance. The application
/// also keeps an instance it never initializes, for what <see cref="IPluginDescriptor"/> offers, so the
/// constructor must not connect to anything.
/// </summary>
public interface IPlugin : IPluginDescriptor, IAsyncDisposable
{
	/// <summary>
	/// Initializes the plugin with configuration. Called at most once per instance.
	/// </summary>
	/// <param name="configuration">Plugin-specific configuration</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if initialization was successful</returns>
	Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Called when the instance is no longer needed, also after an initialization that failed.
	/// It isn't used again afterwards.
	/// </summary>
	Task ShutdownAsync();
}
