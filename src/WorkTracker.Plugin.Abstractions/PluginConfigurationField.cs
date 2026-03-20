namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Configuration field definition for plugin settings
/// </summary>
public class PluginConfigurationField
{
	/// <summary>
	/// Unique key for the configuration value
	/// </summary>
	public required string Key { get; init; }

	/// <summary>
	/// Display label for the field
	/// </summary>
	public required string Label { get; init; }

	/// <summary>
	/// Field description/help text
	/// </summary>
	public string? Description { get; init; }

	/// <summary>
	/// Field type (text, password, url, number, etc.)
	/// </summary>
	public PluginConfigurationFieldType Type { get; init; } = PluginConfigurationFieldType.Text;

	/// <summary>
	/// Whether this field is required
	/// </summary>
	public bool IsRequired { get; init; }

	/// <summary>
	/// Default value for the field
	/// </summary>
	public string? DefaultValue { get; init; }

	/// <summary>
	/// Placeholder text
	/// </summary>
	public string? Placeholder { get; init; }

	/// <summary>
	/// Validation regex pattern
	/// </summary>
	public string? ValidationPattern { get; init; }

	/// <summary>
	/// Validation error message
	/// </summary>
	public string? ValidationMessage { get; init; }
}
