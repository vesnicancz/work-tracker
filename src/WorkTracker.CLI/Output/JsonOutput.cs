using System.Text.Json;
using System.Text.Json.Serialization;
using WorkTracker.Domain.Entities;

namespace WorkTracker.CLI.Output;

/// <summary>
/// Machine-readable projections of command results for the "--json" switch.
/// The shapes here are a stable contract for scripts: fields are added, never renamed or removed.
/// Times are local and formatted round-trip ("s"), durations are whole minutes.
/// </summary>
public static class JsonOutput
{
	private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public sealed record EntryJson(
		int Id,
		string? TicketId,
		string? Description,
		string StartTime,
		string? EndTime,
		int? DurationMinutes,
		bool IsActive);

	public sealed record ListJson(string Date, IReadOnlyList<EntryJson> Entries, int TotalMinutes);

	public sealed record ProviderJson(string Id, string Name, bool Enabled);

	public sealed record StatusJson(bool Active, EntryJson? Entry, int? ElapsedMinutes);

	public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

	public static EntryJson ToEntryJson(WorkEntry entry) => new(
		entry.Id,
		entry.TicketId,
		entry.Description,
		entry.StartTime.ToString(TimeFormat),
		entry.EndTime?.ToString(TimeFormat),
		entry.Duration.HasValue ? (int)entry.Duration.Value.TotalMinutes : null,
		entry.IsActive);

	public static ListJson ToListJson(DateTime date, IEnumerable<WorkEntry> entries)
	{
		var items = entries.Select(ToEntryJson).ToList();
		return new ListJson(
			date.ToString("yyyy-MM-dd"),
			items,
			items.Sum(e => e.DurationMinutes ?? 0));
	}

	public static StatusJson ToStatusJson(WorkEntry? activeEntry, DateTime now) =>
		activeEntry == null
			? new StatusJson(false, null, null)
			: new StatusJson(true, ToEntryJson(activeEntry), (int)(now - activeEntry.StartTime).TotalMinutes);
}
