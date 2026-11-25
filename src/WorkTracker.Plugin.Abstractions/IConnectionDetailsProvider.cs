namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Plugin that other programs connect to. The settings show what to enter in them, ready to copy.
/// </summary>
public interface IConnectionDetailsProvider : IPluginDescriptor
{
	/// <summary>
	/// Gets the connection details for a configuration, which may be unfinished while the user edits it.
	/// Called on every edit on an instance that isn't initialized, so it must be quick and must not throw.
	/// </summary>
	IReadOnlyList<PluginConnectionDetail> GetConnectionDetails(IReadOnlyDictionary<string, string> configuration);
}

/// <param name="Label">What the value is</param>
/// <param name="Value">The value to enter in the other program</param>
/// <param name="IsSecret">Whether the value is masked until the user reveals it</param>
public sealed record PluginConnectionDetail(string Label, string Value, bool IsSecret = false);
