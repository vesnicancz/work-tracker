using System.Text.Json;
using System.Text.Json.Serialization;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck;

/// <summary>
/// The wire format: one JSON object per line. The client sends <see cref="RemoteRequest"/>s; the
/// server answers each with a <see cref="RemoteResponse"/> and, independently of them, pushes a
/// <see cref="RemoteStateMessage"/> on connect and on every state change. Keep in sync with
/// <c>streamdeck-plugin/src/protocol.ts</c>.
/// </summary>
internal static class RemoteControlProtocol
{
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
		public const string StartPomodoro = "startPomodoro";
		public const string StopPomodoro = "stopPomodoro";
		public const string SkipPomodoro = "skipPomodoro";
	}
}

internal sealed record RemoteRequest(long? Id, string? Command, string? TicketId, string? Description, int? Minutes, DateTimeOffset? StartTime, DateTimeOffset? EndTime);

internal sealed record RemoteResponse(long? Id, bool Success, string? Error, RemoteSuggestions? Suggestions = null)
{
	public string Type => "result";
}

internal sealed record RemoteStateMessage(RemoteWorkMessage? ActiveWork, RemoteWorkMessage? LastWork, RemotePomodoroMessage Pomodoro)
{
	public string Type => "state";

	public static RemoteStateMessage From(RemoteControlState state) => new(
		RemoteWorkMessage.From(state.ActiveWork),
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

internal sealed record RemotePomodoroMessage(
	StatusIndicatorState Phase,
	int RemainingSeconds,
	int CompletedPomodoros,
	int PomodorosBeforeLongBreak);
