using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Mcp;

/// <summary>
/// The MCP tools, each a thin call into the <see cref="IRemoteControlHost"/>.
/// </summary>
internal static class WorkTrackerTools
{
	private static readonly TimeSpan LastWorkMaxAge = TimeSpan.FromDays(7);

	public static IReadOnlyList<McpServerTool> Create(IRemoteControlHost host)
	{
		return
		[
			McpServerTool.Create(
				async (CancellationToken cancellationToken) =>
				{
					var state = await host.GetStateAsync(LastWorkMaxAge, cancellationToken);
					return new StatusResult(state.ActiveWork, state.LastWork, state.WorkedTime.Today, state.WorkedTime.Week);
				},
				new McpServerToolCreateOptions
				{
					Name = "get_status",
					Description = "Gets the work being tracked, the most recent work when nothing is, and the time worked today and this week.",
					ReadOnly = true
				}),
			McpServerTool.Create(
				async (
					[Description("The ticket to work on, such as PROJ-123.")] string? ticketId = null,
					[Description("What the work is about.")] string? description = null,
					CancellationToken cancellationToken = default) =>
				{
					return ToToolResult(await host.StartWorkAsync(ticketId, description, cancellationToken), "Work started.");
				},
				new McpServerToolCreateOptions
				{
					Name = "start_work",
					Description = "Starts tracking new work. Stops the work being tracked first. Needs a ticket, a description or both.",
					Destructive = false
				}),
			McpServerTool.Create(
				async (CancellationToken cancellationToken) =>
				{
					return ToToolResult(await host.StopWorkAsync(cancellationToken), "Work stopped.");
				},
				new McpServerToolCreateOptions
				{
					Name = "stop_work",
					Description = "Stops the work being tracked.",
					Destructive = false,
					Idempotent = true
				})
		];
	}

	/// <summary>
	/// A failure goes back as a tool result marked as an error, so the model sees why and can correct itself.
	/// </summary>
	private static CallToolResult ToToolResult(PluginResult result, string successMessage)
	{
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = result.IsSuccess ? successMessage : result.Error ?? "The operation failed." }],
			IsError = result.IsFailure
		};
	}

	internal sealed record StatusResult(RemoteWorkItem? ActiveWork, RemoteWorkItem? LastWork, TimeSpan WorkedToday, TimeSpan WorkedThisWeek);
}
