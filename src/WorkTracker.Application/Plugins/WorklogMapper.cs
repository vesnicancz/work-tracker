using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Plugins;

/// <summary>
/// Maps application worklogs to plugin types
/// </summary>
public static class WorklogMapper
{
	/// <summary>
	/// Converts a WorklogDto to a plugin PluginWorklogEntry
	/// </summary>
	public static PluginWorklogEntry ToPluginWorklog(this WorklogDto worklog)
	{
		return new PluginWorklogEntry
		{
			TicketId = worklog.TicketId,
			Description = worklog.Description,
			StartTime = worklog.StartTime,
			EndTime = worklog.EndTime,
			DurationMinutes = worklog.DurationMinutes
		};
	}

	/// <summary>
	/// Converts a collection of WorklogDto to plugin PluginWorklogEntry
	/// </summary>
	public static IEnumerable<PluginWorklogEntry> ToPluginWorklogs(this IEnumerable<WorklogDto> worklogs)
	{
		return worklogs.Select(ToPluginWorklog);
	}
}
