namespace WorkTracker.Plugin.StreamDeck;

/// <summary>
/// The configuration of the plugin, as the pipe server needs it.
/// </summary>
/// <param name="ResumeWindow">How long ago work may have started to be offered for resuming.</param>
/// <param name="DailyTarget">How much work a day is aimed for. The week aims for five of them.</param>
internal sealed record StreamDeckOptions(TimeSpan ResumeWindow, TimeSpan DailyTarget);
