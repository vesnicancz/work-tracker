using WorkTracker.Application.Common;

namespace WorkTracker.UI.Shared.Orchestrators;

/// <summary>
/// Parses work input to detect Jira ticket ID and description.
/// Format: "PROJECT-123 Description text" or just "Description text"
/// </summary>
public static class WorkInputParser
{
	public static (string? TicketId, string? Description) Parse(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return (null, null);
		}

		var match = JiraPatterns.TicketId().Match(input);
		if (match.Success)
		{
			var ticketId = match.Groups[1].Value;
			var remaining = input.Substring(ticketId.Length).TrimStart();
			return (ticketId, string.IsNullOrWhiteSpace(remaining) ? null : remaining);
		}

		return (null, input);
	}
}