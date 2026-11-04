namespace WorkTracker.CLI.Commands;

/// <summary>
/// What a command needs brought up before it runs. Migrating the database or signing plugins in
/// takes time and can fail, and neither should stand in the way of a command that doesn't use them.
/// </summary>
public readonly record struct CliCommandRequirements(bool Database, bool Plugins)
{
	/// <summary>
	/// Gets the requirements of a command, or null when there is no such command.
	/// </summary>
	public static CliCommandRequirements? Of(string command) => command switch
	{
		"start" or "stop" or "status" or "list" or "edit" or "delete" => new CliCommandRequirements(Database: true, Plugins: false),
		"send" => new CliCommandRequirements(Database: true, Plugins: true),
		"providers" => new CliCommandRequirements(Database: false, Plugins: true),
		_ => null
	};
}
