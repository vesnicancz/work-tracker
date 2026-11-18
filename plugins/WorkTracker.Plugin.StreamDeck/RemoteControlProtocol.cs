using System.Text.Json;
using System.Text.Json.Serialization;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck;

/// <summary>
/// The wire format: one JSON object per line. The client sends <see cref="RemoteRequest"/>s; the
/// server answers each with a <see cref="RemoteResponse"/> and, independently of them, pushes a
/// <see cref="RemoteStateMessage"/> on connect and on every state change. Keep in sync with
/// <c>streamdeck-plugin/src/protocol.ts</c>; the files in <c>streamdeck-plugin/test/fixtures/protocol</c>
/// are checked by the tests of both sides.
/// </summary>
internal static class RemoteControlProtocol
{
	/// <summary>
	/// Raised, here and in <c>protocol.ts</c>, by a change the other side can't cope with. The plugin and
	/// the Stream Deck part are released separately, so they can meet at different versions.
	/// </summary>
	public const int Version = 1;

	public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	public static class Commands
	{
		public const string ToggleWork = "toggleWork";
		public const string StartWork = "startWork";
		public const string StopWork = "stopWork";
		public const string ResumeWork = "resumeWork";
		public const string ShiftStart = "shiftStart";
		public const string AddWork = "addWork";
		public const string GetSuggestions = "getSuggestions";
		public const string GetFavorites = "getFavorites";
		public const string ShowNewWorkDialog = "showNewWorkDialog";
		public const string StartPomodoro = "startPomodoro";
		public const string StopPomodoro = "stopPomodoro";
		public const string SkipPomodoro = "skipPomodoro";
	}
}

internal sealed record RemoteRequest(long? Id, string? Command, string? TicketId, string? Description, int? Minutes, DateTimeOffset? StartTime, DateTimeOffset? EndTime);

internal sealed record RemoteResponse(long? Id, bool Success, string? Error, RemoteSuggestions? Suggestions = null, IReadOnlyList<RemoteFavorite>? Favorites = null)
{
	public string Type => "result";
}

internal sealed record RemoteStateMessage(RemoteWorkMessage? ActiveWork, RemoteScheduledWorkMessage? ScheduledWork, RemoteWorkMessage? LastWork, RemotePomodoroMessage Pomodoro)
{
	public string Type => "state";

	public int Protocol => RemoteControlProtocol.Version;

	public static RemoteStateMessage From(RemoteControlState state) => new(
		RemoteWorkMessage.From(state.ActiveWork),
		RemoteScheduledWorkMessage.From(state.ScheduledWork),
		RemoteWorkMessage.From(state.LastWork),
		new RemotePomodoroMessage(
			state.Pomodoro.Phase,
			(int)state.Pomodoro.TimeRemaining.TotalSeconds,
			state.Pomodoro.CompletedPomodoros,
			state.Pomodoro.PomodorosBeforeLongBreak));
}

internal sealed record RemoteWorkMessage(string? TicketId, string? Description, DateTimeOffset StartedAt)
{
	public static RemoteWorkMessage? From(RemoteWorkItem? item)
	{
		return item == null ? null : new RemoteWorkMessage(item.TicketId, item.Description, item.StartedAt);
	}
}

/// <summary>
/// Added to version 1 without raising it: a client that doesn't know it ignores it.
/// </summary>
internal sealed record RemoteScheduledWorkMessage(string? TicketId, string? Description, DateTimeOffset StartTime, DateTimeOffset EndTime)
{
	public static RemoteScheduledWorkMessage? From(RemoteScheduledWork? work)
	{
		return work == null ? null : new RemoteScheduledWorkMessage(work.TicketId, work.Description, work.StartTime, work.EndTime);
	}
}

internal sealed record RemotePomodoroMessage(
	StatusIndicatorState Phase,
	int RemainingSeconds,
	int CompletedPomodoros,
	int PomodorosBeforeLongBreak);
