namespace WorkTracker.CLI.Output;

/// <summary>
/// How the data commands (list, status, providers) print their results.
/// </summary>
public enum OutputFormat
{
	/// <summary>
	/// Spectre tables for a human at a terminal.
	/// </summary>
	Table,

	/// <summary>
	/// Indented JSON ("--json").
	/// </summary>
	Json,

	/// <summary>
	/// One record per line, tab-separated, no header ("--plain", or stdout redirected).
	/// </summary>
	Plain
}
