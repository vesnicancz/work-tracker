using Spectre.Console;
using WorkTracker.Application.Services;

namespace WorkTracker.CLI.Commands;

public sealed class CommandHandler
{
	private readonly IWorkEntryService _workEntryService;
	private readonly IWorklogSubmissionService _submissionService;
	private readonly TimeProvider _timeProvider;

	public CommandHandler(
		IWorkEntryService workEntryService,
		IWorklogSubmissionService submissionService,
		TimeProvider timeProvider)
	{
		_workEntryService = workEntryService;
		_submissionService = submissionService;
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
				AnsiConsole.MarkupLine($"[red]✗ Error:[/] {result.Error}");
				return 1;
			}

			var entry = result.Value;

			// Show info if previous work was auto-stopped
			if (activeEntry != null)
			{
				AnsiConsole.MarkupLine($"[yellow]⚠[/] Auto-stopped previous work on ticket [bold]{activeEntry.TicketId ?? "N/A"}[/]");
				AnsiConsole.MarkupLine($"  Stopped at: [dim]{entry.StartTime:HH:mm:ss}[/]");
				AnsiConsole.WriteLine();
			}

			var ticketDisplay = string.IsNullOrWhiteSpace(ticketId) ? "[dim]no ticket[/]" : $"[bold]{ticketId}[/]";
			AnsiConsole.MarkupLine($"[green]✓[/] Started work on {ticketDisplay}");

			if (!string.IsNullOrWhiteSpace(description))
			{
				AnsiConsole.MarkupLine($"  Description: [cyan]{description}[/]");
			}

			AnsiConsole.MarkupLine($"  Start time: [yellow]{entry.StartTime:HH:mm:ss}[/]");
			AnsiConsole.MarkupLine($"  Entry ID: [dim]{entry.Id}[/]");

			return 0;
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
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
				AnsiConsole.MarkupLine($"[red]✗ Error:[/] {result.Error}");
				return 1;
			}

			var entry = result.Value;

			var duration = entry.Duration;
			var durationStr = duration.HasValue
				? $"{(int)duration.Value.TotalHours}h {duration.Value.Minutes}m"
				: "N/A";

			var ticketDisplay = string.IsNullOrWhiteSpace(entry.TicketId) ? "[dim]no ticket[/]" : $"[bold]{entry.TicketId}[/]";
			AnsiConsole.MarkupLine($"[green]✓[/] Stopped work on {ticketDisplay}");
			AnsiConsole.MarkupLine($"  End time: [yellow]{entry.EndTime:HH:mm:ss}[/]");
			AnsiConsole.MarkupLine($"  Duration: [cyan]{durationStr}[/]");

			return 0;
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
			return 1;
		}
	}

	public async Task<int> HandleStatusCommand()
	{
		try
		{
			var activeEntry = await _workEntryService.GetActiveWorkAsync();

			if (activeEntry == null)
			{
				AnsiConsole.MarkupLine("[yellow]No active work entry[/]");
				return 0;
			}

			var elapsed = _timeProvider.GetLocalNow().DateTime - activeEntry.StartTime;

			var table = new Table();
			table.AddColumn("Property");
			table.AddColumn("Value");

			table.AddRow("Status", "[green]ACTIVE[/]");
			table.AddRow("Ticket ID", string.IsNullOrWhiteSpace(activeEntry.TicketId) ? "[dim]N/A[/]" : $"[bold]{activeEntry.TicketId}[/]");

			if (!string.IsNullOrWhiteSpace(activeEntry.Description))
			{
				table.AddRow("Description", $"[cyan]{activeEntry.Description}[/]");
			}

			table.AddRow("Start Time", activeEntry.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
			table.AddRow("Elapsed", $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m");
			table.AddRow("Entry ID", activeEntry.Id.ToString());

			AnsiConsole.Write(table);

			return 0;
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
			return 1;
		}
	}

	public async Task<int> HandleListCommand(DateTime? date = null)
	{
		try
		{
			var targetDate = date ?? _timeProvider.GetLocalNow().Date;
			var entries = await _workEntryService.GetWorkEntriesByDateAsync(targetDate);

			AnsiConsole.MarkupLine($"[bold]Work entries for {targetDate:yyyy-MM-dd}:[/]\n");

			if (!entries.Any())
			{
				AnsiConsole.MarkupLine("[yellow]No entries found[/]");
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
				var ticketStr = string.IsNullOrWhiteSpace(entry.TicketId) ? "[dim]N/A[/]" : entry.TicketId;
				var descStr = string.IsNullOrWhiteSpace(entry.Description) ? "[dim]-[/]" : entry.Description;

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

			AnsiConsole.Write(table);

			var totalMinutes = entries
				.Where(e => e.Duration.HasValue)
				.Sum(e => e.Duration!.Value.TotalMinutes);

			AnsiConsole.MarkupLine($"\n[bold]Total:[/] {(int)(totalMinutes / 60)}h {(int)(totalMinutes % 60)}m");

			return 0;
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
			return 1;
		}
	}

	public async Task<int> HandleEditCommand(int id, string? ticketId = null,
		DateTime? startTime = null, DateTime? endTime = null, string? description = null)
	{
		try
		{
			var result = await _workEntryService.UpdateWorkEntryAsync(id, ticketId, startTime, endTime, description);

			if (result.IsFailure)
			{
				AnsiConsole.MarkupLine($"[red]✗ Error:[/] {result.Error}");
				return 1;
			}

			var entry = result.Value;

			AnsiConsole.MarkupLine($"[green]✓[/] Updated work entry [bold]#{id}[/]");
			var ticketDisplay = string.IsNullOrWhiteSpace(entry.TicketId) ? "[dim]N/A[/]" : $"[bold]{entry.TicketId}[/]";
			AnsiConsole.MarkupLine($"  Ticket: {ticketDisplay}");

			if (!string.IsNullOrWhiteSpace(entry.Description))
			{
				AnsiConsole.MarkupLine($"  Description: [cyan]{entry.Description}[/]");
			}

			AnsiConsole.MarkupLine($"  Time: {entry.StartTime:HH:mm} - {entry.EndTime:HH:mm}");

			return 0;
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
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
				AnsiConsole.MarkupLine($"[red]✗ Error:[/] {result.Error}");
				return 1;
			}

			AnsiConsole.MarkupLine($"[green]✓[/] Deleted work entry [bold]#{id}[/]");
			return 0;
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
			return 1;
		}
	}

	public async Task<int> HandleSendCommand(DateTime? date = null, bool isWeek = false)
	{
		try
		{
			if (isWeek)
			{
				return await HandleSendWeekCommand(date);
			}

			var targetDate = date ?? _timeProvider.GetLocalNow().Date;

			var preview = await _submissionService.PreviewDailyWorklogAsync(targetDate);

			if (!preview.Worklogs.Any())
			{
				AnsiConsole.MarkupLine("[yellow]No completed entries to send[/]");
				return 0;
			}

			AnsiConsole.MarkupLine($"[bold]Preview of entries to send for {targetDate:yyyy-MM-dd}:[/]\n");

			var table = new Table();
			table.AddColumn("Ticket");
			table.AddColumn("Start");
			table.AddColumn("End");
			table.AddColumn("Duration");

			foreach (var worklog in preview.Worklogs)
			{
				table.AddRow(
					worklog.TicketId ?? "[dim]N/A[/]",
					worklog.StartTime.ToString("HH:mm"),
					worklog.EndTime.ToString("HH:mm"),
					$"{worklog.DurationMinutes / 60}h {worklog.DurationMinutes % 60}m"
				);
			}

			AnsiConsole.Write(table);

			if (!AnsiConsole.Confirm("\nSend these entries to Tempo?"))
			{
				AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
				return 0;
			}

			var result = await _submissionService.SubmitDailyWorklogAsync(targetDate);

			if (result.IsSuccess)
			{
				AnsiConsole.MarkupLine($"[green]✓[/] Successfully sent {result.Value.SuccessfulEntries} entries to Tempo");
				return 0;
			}
			else
			{
				AnsiConsole.MarkupLine($"[red]✗ Failed to send entries to Tempo: {result.Error}[/]");
				return 1;
			}
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
			return 1;
		}
	}

	private async Task<int> HandleSendWeekCommand(DateTime? date = null)
	{
		try
		{
			var targetDate = date ?? _timeProvider.GetLocalNow().Date;

			var preview = await _submissionService.PreviewWeeklyWorklogAsync(targetDate);

			if (!preview.Any())
			{
				AnsiConsole.MarkupLine("[yellow]No completed entries to send for the week[/]");
				return 0;
			}

			var weekStart = preview.Keys.Min();
			var weekEnd = preview.Keys.Max();

			AnsiConsole.MarkupLine($"[bold]Preview of entries to send for week {weekStart:yyyy-MM-dd} to {weekEnd:yyyy-MM-dd}:[/]\n");

			var totalEntries = 0;
			var totalMinutes = 0;

			foreach (var (dayDate, dayPreview) in preview.OrderBy(kvp => kvp.Key))
			{
				if (!dayPreview.Worklogs.Any())
				{
					continue;
				}

				AnsiConsole.MarkupLine($"\n[bold cyan]{dayDate:ddd yyyy-MM-dd}[/]");

				var table = new Table();
				table.Border = TableBorder.Minimal;
				table.AddColumn("Ticket");
				table.AddColumn("Start");
				table.AddColumn("End");
				table.AddColumn("Duration");

				foreach (var worklog in dayPreview.Worklogs)
				{
					table.AddRow(
						worklog.TicketId ?? "[dim]N/A[/]",
						worklog.StartTime.ToString("HH:mm"),
						worklog.EndTime.ToString("HH:mm"),
						$"{worklog.DurationMinutes / 60}h {worklog.DurationMinutes % 60}m"
					);
					totalMinutes += worklog.DurationMinutes;
					totalEntries++;
				}

				AnsiConsole.Write(table);
			}

			AnsiConsole.MarkupLine($"\n[bold]Total:[/] {totalEntries} entries, {totalMinutes / 60}h {totalMinutes % 60}m");

			if (!AnsiConsole.Confirm("\nSend all these entries to Tempo?"))
			{
				AnsiConsole.MarkupLine("[yellow]Cancelled[/]");
				return 0;
			}

			var result = await _submissionService.SubmitWeeklyWorklogAsync(targetDate);

			if (result.IsSuccess)
			{
				var submissionResult = result.Value;
				AnsiConsole.MarkupLine($"[green]✓[/] Successfully sent {submissionResult.SuccessfulEntries} entries to Tempo");

				if (submissionResult.HasPartialSuccess)
				{
					AnsiConsole.MarkupLine($"[yellow]⚠[/] {submissionResult.FailedEntries} entries failed");
					foreach (var error in submissionResult.Errors)
					{
						AnsiConsole.MarkupLine($"  [red]-[/] {error.Date:yyyy-MM-dd}: {error.ErrorMessage}");
					}
				}

				return 0;
			}
			else
			{
				AnsiConsole.MarkupLine($"[red]✗ Failed to send entries to Tempo: {result.Error}[/]");
				return 1;
			}
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
			return 1;
		}
	}
}