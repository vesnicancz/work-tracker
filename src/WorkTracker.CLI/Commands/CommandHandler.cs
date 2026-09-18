using Spectre.Console;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.CLI.Output;

namespace WorkTracker.CLI.Commands;

public sealed class CommandHandler
{
	private readonly IWorkEntryService _workEntryService;
	private readonly IWorklogSubmissionService _submissionService;
	private readonly IPluginManager _pluginManager;
	private readonly TimeProvider _timeProvider;

	public CommandHandler(
		IWorkEntryService workEntryService,
		IWorklogSubmissionService submissionService,
		IPluginManager pluginManager,
		TimeProvider timeProvider)
	{
		_workEntryService = workEntryService;
		_submissionService = submissionService;
		_pluginManager = pluginManager;
		_timeProvider = timeProvider;
	}

	public async Task<int> HandleStartCommand(string? ticketId, DateTime? startTime = null, string? description = null)
	{
		try
		{
			// Check if there's an active work entry that will be auto-stopped
			var activeEntry = await _workEntryService.GetActiveWorkAsync();

			var result = await _workEntryService.StartWorkAsync(ticketId, startTime, description);

			if (result.IsFailure)
			{
				CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(result.Error)}");
				return 1;
			}

			var entry = result.Value;

			// Show info if previous work was auto-stopped
			if (activeEntry != null)
			{
				CliConsole.Out.MarkupLine($"[yellow]⚠[/] Auto-stopped previous work on ticket [bold]{Markup.Escape(activeEntry.TicketId ?? "N/A")}[/]");
				CliConsole.Out.MarkupLine($"  Stopped at: [dim]{entry.StartTime:HH:mm:ss}[/]");
				CliConsole.Out.WriteLine();
			}

			var ticketDisplay = string.IsNullOrWhiteSpace(ticketId) ? "[dim]no ticket[/]" : $"[bold]{Markup.Escape(ticketId)}[/]";
			CliConsole.Out.MarkupLine($"[green]✓[/] Started work on {ticketDisplay}");

			if (!string.IsNullOrWhiteSpace(description))
			{
				CliConsole.Out.MarkupLine($"  Description: [cyan]{Markup.Escape(description)}[/]");
			}

			CliConsole.Out.MarkupLine($"  Start time: [yellow]{entry.StartTime:HH:mm:ss}[/]");
			CliConsole.Out.MarkupLine($"  Entry ID: [dim]{entry.Id}[/]");

			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	public async Task<int> HandleStopCommand(DateTime? endTime = null)
	{
		try
		{
			var result = await _workEntryService.StopWorkAsync(endTime);

			if (result.IsFailure)
			{
				CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(result.Error)}");
				return 1;
			}

			var entry = result.Value;

			var duration = entry.Duration;
			var durationStr = duration.HasValue
				? $"{(int)duration.Value.TotalHours}h {duration.Value.Minutes}m"
				: "N/A";

			var ticketDisplay = string.IsNullOrWhiteSpace(entry.TicketId) ? "[dim]no ticket[/]" : $"[bold]{Markup.Escape(entry.TicketId)}[/]";
			CliConsole.Out.MarkupLine($"[green]✓[/] Stopped work on {ticketDisplay}");
			CliConsole.Out.MarkupLine($"  End time: [yellow]{entry.EndTime:HH:mm:ss}[/]");
			CliConsole.Out.MarkupLine($"  Duration: [cyan]{durationStr}[/]");

			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	public async Task<int> HandleStatusCommand(bool json = false)
	{
		try
		{
			var activeEntry = await _workEntryService.GetActiveWorkAsync();

			if (json)
			{
				CliConsole.Data.WriteLine(JsonOutput.Serialize(
					JsonOutput.ToStatusJson(activeEntry, _timeProvider.GetLocalNow().DateTime)));
				return 0;
			}

			if (activeEntry == null)
			{
				CliConsole.Out.MarkupLine("[yellow]No active work entry[/]");
				return 0;
			}

			var elapsed = _timeProvider.GetLocalNow().DateTime - activeEntry.StartTime;

			var table = new Table();
			table.AddColumn("Property");
			table.AddColumn("Value");

			table.AddRow("Status", "[green]ACTIVE[/]");
			table.AddRow("Ticket ID", string.IsNullOrWhiteSpace(activeEntry.TicketId) ? "[dim]N/A[/]" : $"[bold]{Markup.Escape(activeEntry.TicketId)}[/]");

			if (!string.IsNullOrWhiteSpace(activeEntry.Description))
			{
				table.AddRow("Description", $"[cyan]{Markup.Escape(activeEntry.Description)}[/]");
			}

			table.AddRow("Start Time", activeEntry.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
			table.AddRow("Elapsed", $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m");
			table.AddRow("Entry ID", activeEntry.Id.ToString());

			CliConsole.Out.Write(table);

			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	public async Task<int> HandleListCommand(DateTime? date = null, bool json = false)
	{
		try
		{
			var targetDate = date ?? _timeProvider.GetLocalNow().Date;
			var entries = await _workEntryService.GetWorkEntriesByDateAsync(targetDate);

			if (json)
			{
				CliConsole.Data.WriteLine(JsonOutput.Serialize(JsonOutput.ToListJson(targetDate, entries)));
				return 0;
			}

			CliConsole.Out.MarkupLine($"[bold]Work entries for {targetDate:yyyy-MM-dd}:[/]\n");

			if (!entries.Any())
			{
				CliConsole.Out.MarkupLine("[yellow]No entries found[/]");
				return 0;
			}

			var table = new Table();
			table.AddColumn("ID");
			table.AddColumn("Ticket");
			table.AddColumn("Description");
			table.AddColumn("Start");
			table.AddColumn("End");
			table.AddColumn("Duration");
			table.AddColumn("Status");

			foreach (var entry in entries)
			{
				var duration = entry.Duration;
				var durationStr = duration.HasValue
					? $"{(int)duration.Value.TotalHours}h {duration.Value.Minutes}m"
					: "-";

				var status = entry.IsActive ? "[green]ACTIVE[/]" : "[dim]completed[/]";
				var endTimeStr = entry.EndTime?.ToString("HH:mm") ?? "-";
				var ticketStr = string.IsNullOrWhiteSpace(entry.TicketId) ? "[dim]N/A[/]" : Markup.Escape(entry.TicketId);
				var descStr = string.IsNullOrWhiteSpace(entry.Description) ? "[dim]-[/]" : Markup.Escape(entry.Description);

				table.AddRow(
					entry.Id.ToString(),
					ticketStr,
					descStr,
					entry.StartTime.ToString("HH:mm"),
					endTimeStr,
					durationStr,
					status
				);
			}

			CliConsole.Out.Write(table);

			var totalMinutes = entries
				.Where(e => e.Duration.HasValue)
				.Sum(e => e.Duration!.Value.TotalMinutes);

			CliConsole.Out.MarkupLine($"\n[bold]Total:[/] {(int)(totalMinutes / 60)}h {(int)(totalMinutes % 60)}m");

			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	public async Task<int> HandleEditCommand(int id, string? ticketId = null,
		DateTime? startTime = null, DateTime? endTime = null, string? description = null)
	{
		try
		{
			if (ticketId is null && startTime is null && endTime is null && description is null)
			{
				CliConsole.Error.MarkupLine("[red]✗ Error:[/] Nothing to change — pass at least one option");
				CliConsole.Error.MarkupLine(Markup.Escape("Options: --ticket=<ticket> --start=<time> --end=<time> --desc=<description>"));
				return 1;
			}

			// UpdateWorkEntryAsync replaces every field (the GUI edit dialog always submits a whole
			// entry), so the options the user left out have to be carried over from the stored entry.
			// Without this a partial edit such as "--desc=..." would wipe the ticket and end time.
			var existing = await _workEntryService.GetWorkEntryByIdAsync(id);
			if (existing == null)
			{
				CliConsole.Error.MarkupLine($"[red]✗ Error:[/] Work entry with ID {id} not found");
				return 1;
			}

			var result = await _workEntryService.UpdateWorkEntryAsync(
				id,
				ticketId ?? existing.TicketId,
				startTime ?? existing.StartTime,
				endTime ?? existing.EndTime,
				description ?? existing.Description);

			if (result.IsFailure)
			{
				CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(result.Error)}");
				return 1;
			}

			var entry = result.Value;

			CliConsole.Out.MarkupLine($"[green]✓[/] Updated work entry [bold]#{id}[/]");
			var ticketDisplay = string.IsNullOrWhiteSpace(entry.TicketId) ? "[dim]N/A[/]" : $"[bold]{Markup.Escape(entry.TicketId)}[/]";
			CliConsole.Out.MarkupLine($"  Ticket: {ticketDisplay}");

			if (!string.IsNullOrWhiteSpace(entry.Description))
			{
				CliConsole.Out.MarkupLine($"  Description: [cyan]{Markup.Escape(entry.Description)}[/]");
			}

			CliConsole.Out.MarkupLine($"  Time: {entry.StartTime:HH:mm} - {entry.EndTime:HH:mm}");

			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	public async Task<int> HandleDeleteCommand(int id)
	{
		try
		{
			var result = await _workEntryService.DeleteWorkEntryAsync(id);

			if (result.IsFailure)
			{
				CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(result.Error)}");
				return 1;
			}

			CliConsole.Out.MarkupLine($"[green]✓[/] Deleted work entry [bold]#{id}[/]");
			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	/// <summary>
	/// Lists the worklog upload plugins, disabled ones included — a plugin that is installed but
	/// not enabled in the GUI is the usual reason "send" reports no provider.
	/// </summary>
	public int HandleProvidersCommand(bool json = false)
	{
		try
		{
			var enabledIds = _pluginManager.WorklogUploadPlugins
				.Select(p => p.Metadata.Id)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var providers = _pluginManager.AllWorklogUploadPlugins
				.Select(p => (p.Metadata.Id, p.Metadata.Name, Enabled: enabledIds.Contains(p.Metadata.Id)))
				.OrderByDescending(p => p.Enabled)
				.ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (json)
			{
				CliConsole.Data.WriteLine(JsonOutput.Serialize(
					providers.Select(p => new JsonOutput.ProviderJson(p.Id, p.Name, p.Enabled)).ToList()));
				return 0;
			}

			if (providers.Count == 0)
			{
				CliConsole.Out.MarkupLine("[yellow]No worklog upload plugins installed[/]");
				CliConsole.Out.MarkupLine("  Drop a plugin into the [cyan]plugins/[/] directory next to the executable.");
				return 0;
			}

			var table = new Table();
			table.AddColumn("ID");
			table.AddColumn("Name");
			table.AddColumn("Status");

			foreach (var (id, name, enabled) in providers)
			{
				table.AddRow(
					Markup.Escape(id),
					Markup.Escape(name),
					enabled ? "[green]enabled[/]" : "[dim]disabled[/]");
			}

			CliConsole.Out.Write(table);

			if (!providers.Any(p => p.Enabled))
			{
				CliConsole.Out.MarkupLine("\n[yellow]No provider is enabled[/] — enable one in the desktop app's settings.");
			}

			return 0;
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	public async Task<int> HandleSendCommand(DateTime? date = null, bool isWeek = false, bool assumeYes = false, string? providerId = null)
	{
		try
		{
			if (isWeek)
			{
				return await HandleSendWeekCommand(date, assumeYes, providerId);
			}

			var targetDate = date ?? _timeProvider.GetLocalNow().Date;

			var preview = await _submissionService.PreviewDailyWorklogAsync(targetDate);

			if (!preview.Worklogs.Any())
			{
				CliConsole.Out.MarkupLine("[yellow]No completed entries to send[/]");
				return 0;
			}

			CliConsole.Out.MarkupLine($"[bold]Preview of entries to send for {targetDate:yyyy-MM-dd}:[/]\n");

			var table = new Table();
			table.AddColumn("Ticket");
			table.AddColumn("Start");
			table.AddColumn("End");
			table.AddColumn("Duration");

			foreach (var worklog in preview.Worklogs)
			{
				table.AddRow(
					string.IsNullOrWhiteSpace(worklog.TicketId) ? "[dim]N/A[/]" : Markup.Escape(worklog.TicketId),
					worklog.StartTime.ToString("HH:mm"),
					worklog.EndTime.ToString("HH:mm"),
					$"{worklog.DurationMinutes / 60}h {worklog.DurationMinutes % 60}m"
				);
			}

			CliConsole.Out.Write(table);

			if (!assumeYes && !CliConsole.Out.Confirm("\nSend these entries to Tempo?"))
			{
				CliConsole.Out.MarkupLine("[yellow]Cancelled[/]");
				return 0;
			}

			var result = providerId == null
				? await _submissionService.SubmitDailyWorklogAsync(targetDate)
				: await _submissionService.SubmitDailyWorklogAsync(targetDate, providerId);

			if (result.IsSuccess)
			{
				CliConsole.Out.MarkupLine($"[green]✓[/] Successfully sent {result.Value.SuccessfulEntries} entries to Tempo");
				return 0;
			}
			else
			{
				CliConsole.Error.MarkupLine($"[red]✗ Failed to send entries to Tempo:[/] {Markup.Escape(result.Error)}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	private async Task<int> HandleSendWeekCommand(DateTime? date = null, bool assumeYes = false, string? providerId = null)
	{
		try
		{
			var targetDate = date ?? _timeProvider.GetLocalNow().Date;

			var preview = await _submissionService.PreviewWeeklyWorklogAsync(targetDate);

			// The preview always holds one entry per day of the week, so it is never empty —
			// the week has nothing to send only when every one of those days is empty.
			if (!preview.Values.Any(day => day.Worklogs.Any()))
			{
				CliConsole.Out.MarkupLine("[yellow]No completed entries to send for the week[/]");
				return 0;
			}

			var weekStart = preview.Keys.Min();
			var weekEnd = preview.Keys.Max();

			CliConsole.Out.MarkupLine($"[bold]Preview of entries to send for week {weekStart:yyyy-MM-dd} to {weekEnd:yyyy-MM-dd}:[/]\n");

			var totalEntries = 0;
			var totalMinutes = 0;

			foreach (var (dayDate, dayPreview) in preview.OrderBy(kvp => kvp.Key))
			{
				if (!dayPreview.Worklogs.Any())
				{
					continue;
				}

				CliConsole.Out.MarkupLine($"\n[bold cyan]{dayDate:ddd yyyy-MM-dd}[/]");

				var table = new Table();
				table.Border = TableBorder.Minimal;
				table.AddColumn("Ticket");
				table.AddColumn("Start");
				table.AddColumn("End");
				table.AddColumn("Duration");

				foreach (var worklog in dayPreview.Worklogs)
				{
					table.AddRow(
						string.IsNullOrWhiteSpace(worklog.TicketId) ? "[dim]N/A[/]" : Markup.Escape(worklog.TicketId),
						worklog.StartTime.ToString("HH:mm"),
						worklog.EndTime.ToString("HH:mm"),
						$"{worklog.DurationMinutes / 60}h {worklog.DurationMinutes % 60}m"
					);
					totalMinutes += worklog.DurationMinutes;
					totalEntries++;
				}

				CliConsole.Out.Write(table);
			}

			CliConsole.Out.MarkupLine($"\n[bold]Total:[/] {totalEntries} entries, {totalMinutes / 60}h {totalMinutes % 60}m");

			if (!assumeYes && !CliConsole.Out.Confirm("\nSend all these entries to Tempo?"))
			{
				CliConsole.Out.MarkupLine("[yellow]Cancelled[/]");
				return 0;
			}

			var result = providerId == null
				? await _submissionService.SubmitWeeklyWorklogAsync(targetDate)
				: await _submissionService.SubmitWeeklyWorklogAsync(targetDate, providerId);

			if (result.IsSuccess)
			{
				var submissionResult = result.Value;
				CliConsole.Out.MarkupLine($"[green]✓[/] Successfully sent {submissionResult.SuccessfulEntries} entries to Tempo");

				if (submissionResult.HasPartialSuccess)
				{
					CliConsole.Error.MarkupLine($"[yellow]⚠[/] {submissionResult.FailedEntries} entries failed");
					foreach (var error in submissionResult.Errors)
					{
						CliConsole.Error.MarkupLine($"  [red]-[/] {error.Date:yyyy-MM-dd}: {Markup.Escape(error.ErrorMessage)}");
					}
				}

				return 0;
			}
			else
			{
				CliConsole.Error.MarkupLine($"[red]✗ Failed to send entries to Tempo:[/] {Markup.Escape(result.Error)}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}
}