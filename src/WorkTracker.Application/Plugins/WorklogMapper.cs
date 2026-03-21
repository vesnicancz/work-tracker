using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Plugins;

/// <summary>
/// Maps between domain types and plugin types
/// </summary>
public static class WorklogMapper
{
	/// <summary>
	/// Converts a domain WorklogDto to a plugin PluginWorklogEntry
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
	/// Converts a plugin PluginWorklogEntry to a domain WorklogDto
	/// </summary>
	public static WorklogDto ToDomainWorklog(this PluginWorklogEntry worklog)
	{
		return new WorklogDto
		{
			TicketId = worklog.TicketId,
			Description = worklog.Description,
			StartTime = worklog.StartTime,
			EndTime = worklog.EndTime,
			DurationMinutes = worklog.DurationMinutes
		};
	}

	/// <summary>
	/// Converts a collection of domain WorklogDto to plugin PluginWorklogEntry
	/// </summary>
	public static IEnumerable<PluginWorklogEntry> ToPluginWorklogs(this IEnumerable<WorklogDto> worklogs)
	{
		return worklogs.Select(ToPluginWorklog);
	}

	/// <summary>
	/// Converts a collection of plugin PluginWorklogEntry to domain WorklogDto
	/// </summary>
	public static IEnumerable<WorklogDto> ToDomainWorklogs(this IEnumerable<PluginWorklogEntry> worklogs)
	{
		return worklogs.Select(ToDomainWorklog);
	}

	/// <summary>
	/// Converts a plugin PluginResult to a domain Result
	/// </summary>
	public static Result<T> ToDomainResult<T>(this PluginResult<T> pluginResult)
	{
		if (pluginResult.IsSuccess && pluginResult.Value != null)
		{
			return Result.Success(pluginResult.Value);
		}

		return Result.Failure<T>(pluginResult.Error ?? "Unknown error");
	}

	/// <summary>
	/// Converts a domain Result to a plugin PluginResult
	/// </summary>
	public static PluginResult<T> ToPluginResult<T>(this Result<T> result)
	{
		if (result.IsSuccess && result.Value != null)
		{
			return PluginResult<T>.Success(result.Value);
		}

		return PluginResult<T>.Failure(result.Error ?? "Unknown error");
	}
}
