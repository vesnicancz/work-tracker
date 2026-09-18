using Spectre.Console;

namespace WorkTracker.CLI.Output;

/// <summary>
/// The two console streams the CLI writes to. Results go to stdout so they can be piped or
/// redirected; diagnostics go to stderr so "2&gt;/dev/null" leaves a clean, parseable stdout.
/// </summary>
public static class CliConsole
{
	private static IAnsiConsole? _error;

	/// <summary>
	/// Stdout — command results (tables, JSON, confirmations).
	/// </summary>
	public static IAnsiConsole Out => AnsiConsole.Console;

	/// <summary>
	/// Raw stdout for machine-readable output. Deliberately bypasses Spectre: JSON must never be
	/// wrapped at the console width, styled, or have its brackets read as markup.
	/// Settable so tests can capture it.
	/// </summary>
	public static TextWriter Data { get; set; } = Console.Out;

	/// <summary>
	/// Stderr — error messages and usage hints. Settable so tests can capture the stream.
	/// </summary>
	public static IAnsiConsole Error
	{
		get => _error ??= AnsiConsole.Create(new AnsiConsoleSettings
		{
			Out = new AnsiConsoleOutput(Console.Error)
		});
		set => _error = value;
	}
}
