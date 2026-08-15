using WorkTracker.Application.Common;

namespace WorkTracker.CLI.Commands;

/// <summary>
/// Pure command-line argument parsing used by the Program.cs dispatch.
/// Kept separate from the top-level program so the parsing rules are unit-testable.
/// </summary>
public static class CliArgumentParser
{
	public sealed record EditOptions(string? TicketId, DateTime? StartTime, DateTime? EndTime, string? Description);

	public static DateTime? ParseDateTime(string input)
	{
		// Try parsing as full DateTime first (e.g., "2025-10-30 14:30")
		if (DateTime.TryParse(input, out var fullDateTime))
		{
			return fullDateTime;
		}

		// Try parsing as time only (e.g., "14:30" or "14:30:00")
		if (TimeOnly.TryParse(input, out var timeOnly))
		{
			// Combine with today's date
			return DateTime.Today.Add(timeOnly.ToTimeSpan());
		}

		return null;
	}

	/// <summary>
	/// Parses "start" arguments: optional Jira ticket at the beginning, optional time
	/// (one or two trailing tokens), everything in between is the description.
	/// </summary>
	public static (string? TicketId, string? Description, DateTime? StartTime) ParseStartCommandInput(string[] args)
	{
		var jiraPattern = JiraPatterns.TicketId();
		string? ticketId = null;
		string? description = null;
		DateTime? startTime = null;

		// Combine all args starting from index 1
		var input = string.Join(" ", args.Skip(1));

		// Try to extract Jira code from the beginning
		var match = jiraPattern.Match(input);
		if (match.Success)
		{
			ticketId = match.Groups[1].Value;
			input = input.Substring(ticketId.Length).TrimStart();
		}

		if (!string.IsNullOrWhiteSpace(input))
		{
			var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

			DateTime? parsedTime = null;
			int timePartIndex = -1;

			// Check last 2 parts first (case: "description yyyy-MM-dd HH:mm") — must run before
			// the single-part check, because a trailing "HH:mm" always parses on its own and
			// would leave the date stranded in the description
			if (parts.Length > 1)
			{
				var lastTwoParts = $"{parts[^2]} {parts[^1]}";
				parsedTime = ParseDateTime(lastTwoParts);
				if (parsedTime.HasValue)
				{
					timePartIndex = parts.Length - 2;
					startTime = parsedTime;
				}
			}

			// Fall back to the last part alone (most common case: "description HH:mm")
			if (!parsedTime.HasValue && parts.Length > 0)
			{
				parsedTime = ParseDateTime(parts[^1]);
				if (parsedTime.HasValue)
				{
					timePartIndex = parts.Length - 1;
					startTime = parsedTime;
				}
			}

			// Everything before the time part is the description
			if (timePartIndex > 0)
			{
				description = string.Join(" ", parts.Take(timePartIndex));
			}
			else if (timePartIndex == -1)
			{
				description = input;
			}
		}

		return (ticketId, description, startTime);
	}

	/// <summary>
	/// Parses "edit" option flags (--ticket=, --start=, --end=, --desc=) from args[2..].
	/// Returns null when a --start/--end value is invalid; <paramref name="invalidField"/>
	/// is then "start" or "end". Unknown flags are ignored.
	/// </summary>
	public static EditOptions? ParseEditOptions(string[] args, out string? invalidField)
	{
		invalidField = null;
		string? ticketId = null;
		DateTime? startTime = null;
		DateTime? endTime = null;
		string? description = null;

		for (int i = 2; i < args.Length; i++)
		{
			var arg = args[i];
			if (arg.StartsWith("--ticket="))
			{
				ticketId = arg.Substring("--ticket=".Length);
			}
			else if (arg.StartsWith("--start="))
			{
				startTime = ParseDateTime(arg.Substring("--start=".Length));
				if (startTime == null)
				{
					invalidField = "start";
					return null;
				}
			}
			else if (arg.StartsWith("--end="))
			{
				endTime = ParseDateTime(arg.Substring("--end=".Length));
				if (endTime == null)
				{
					invalidField = "end";
					return null;
				}
			}
			else if (arg.StartsWith("--desc="))
			{
				description = arg.Substring("--desc=".Length);
			}
		}

		return new EditOptions(ticketId, startTime, endTime, description);
	}

	/// <summary>
	/// Parses "send" arguments: optional "week" keyword followed by an optional date,
	/// or just an optional date. Returns false when a date token fails to parse.
	/// </summary>
	public static bool TryParseSendArguments(string[] args, out DateTime? date, out bool isWeek)
	{
		date = null;
		isWeek = false;

		if (args.Length >= 2 && args[1].Equals("week", StringComparison.OrdinalIgnoreCase))
		{
			isWeek = true;

			if (args.Length >= 3)
			{
				if (DateTime.TryParse(args[2], out var parsedDate))
				{
					date = parsedDate;
				}
				else
				{
					return false;
				}
			}
		}
		else if (args.Length >= 2)
		{
			if (DateTime.TryParse(args[1], out var parsedDate))
			{
				date = parsedDate;
			}
			else
			{
				return false;
			}
		}

		return true;
	}
}
