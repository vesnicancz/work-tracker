namespace WorkTracker.Plugin.StreamDeck;

/// <summary>
/// Where the Stream Deck plugin finds the pipe. Keep in sync with <c>streamdeck-plugin/src/pipe-path.ts</c>.
/// </summary>
internal static class PipeEndpoint
{
	/// <summary>
	/// On Unix a pipe is a socket file. Passing the full path keeps it where the other side can
	/// compute it, instead of behind the prefix .NET adds to a bare name.
	/// </summary>
	public static string Name => OperatingSystem.IsWindows()
		? "worktracker-streamdeck"
		: Path.Combine(Path.GetTempPath(), "worktracker-streamdeck.sock");
}
