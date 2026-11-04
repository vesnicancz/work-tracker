using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.CLI.Commands;

public enum CliPluginUse
{
	None,

	/// <summary>
	/// Only what a plugin says about itself. Nothing connects or signs in.
	/// </summary>
	Listing,

	/// <summary>
	/// The worklog upload plugin the command sends to.
	/// </summary>
	Upload
}

/// <summary>
/// What a command needs brought up before it runs. Migrating the database or signing plugins in
/// takes time and can fail, and neither should stand in the way of a command that doesn't use them.
/// </summary>
public readonly record struct CliCommandRequirements(bool Database, CliPluginUse Plugins)
{
	/// <summary>
	/// Gets the requirements of a command, or null when there is no such command.
	/// </summary>
	public static CliCommandRequirements? Of(string command) => command switch
	{
		"start" or "stop" or "status" or "list" or "edit" or "delete" => new CliCommandRequirements(Database: true, Plugins: CliPluginUse.None),
		"send" => new CliCommandRequirements(Database: true, Plugins: CliPluginUse.Upload),
		"providers" => new CliCommandRequirements(Database: false, Plugins: CliPluginUse.Listing),
		_ => null
	};

	/// <summary>
	/// Whether the command has to start this plugin. Starting one has effects outside of this process:
	/// an upload plugin connects to its server, and a status indicator takes over a LED the running GUI
	/// may be using for its Pomodoro.
	/// </summary>
	/// <param name="plugin">The plugin in question.</param>
	/// <param name="providerId">The plugin chosen with --provider, if any.</param>
	/// <param name="enabledUploadPlugins">How many upload plugins are enabled. With several and none chosen the command fails anyway, and shouldn't connect to all of them first.</param>
	public bool ShouldStart(IPluginDescriptor plugin, string? providerId, int enabledUploadPlugins)
	{
		if (Plugins != CliPluginUse.Upload || plugin is not IWorklogUploadPluginDescriptor)
		{
			return false;
		}

		return providerId == null
			? enabledUploadPlugins == 1
			: plugin.Metadata.Id == providerId;
	}
}
