using WorkTracker.Domain.Entities;

namespace WorkTracker.CLI.Output;

/// <summary>
/// Line-oriented projections of command results for pipes (grep, cut, awk): one record per line,
/// tab-separated columns, no header. A missing value is "-", durations are whole minutes.
/// </summary>
public static class PlainOutput
{
	private const string Missing = "-";

	/// <summary>
	/// ID, ticket, description, start, end, minutes, active|completed.
	/// </summary>
	public static string FormatEntry(WorkEntry entry, int? minutes) => Join(
		entry.Id.ToString(),
		entry.TicketId,
		entry.Description,
		entry.StartTime.ToString("HH:mm"),
		entry.EndTime?.ToString("HH:mm"),
		minutes?.ToString(),
		entry.IsActive ? "active" : "completed");

	public static string FormatEntry(WorkEntry entry) =>
		FormatEntry(entry, entry.Duration.HasValue ? (int)entry.Duration.Value.TotalMinutes : null);

	/// <summary>
	/// ID, name, enabled|disabled.
	/// </summary>
	public static string FormatProvider(string id, string name, bool enabled) =>
		Join(id, name, enabled ? "enabled" : "disabled");

	private static string Join(params string?[] fields) =>
		string.Join('\t', fields.Select(Sanitize));

	/// <summary>
	/// Keeps a record on one line: tabs and line breaks in free text would split it.
	/// </summary>
	private static string Sanitize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Missing;
		}

		return value.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
	}
}
