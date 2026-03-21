using System.Text.RegularExpressions;

namespace WorkTracker.Application.Common;

/// <summary>
/// Shared Jira-related patterns used across CLI and WPF presentation layers
/// </summary>
public static partial class JiraPatterns
{
	/// <summary>
	/// Matches a Jira ticket ID at the beginning of input (e.g., "PROJ-123", "ABC-1")
	/// </summary>
	[GeneratedRegex(@"^([a-zA-Z0-9]+-[0-9]+)", RegexOptions.Compiled)]
	public static partial Regex TicketId();
}
