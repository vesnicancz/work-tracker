using Spectre.Console;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Plugins;
using WorkTracker.Application.Services;
using WorkTracker.CLI.Output;
using WorkTracker.Plugin.Abstractions;

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

	public async Task<int> HandleStatusCommand(OutputFormat format = OutputFormat.Table)
	{
		try
		{
			var activeEntry = await _workEntryService.GetActiveWorkAsync();

			if (format == OutputFormat.Json)
			{
				CliConsole.Data.WriteLine(JsonOutput.Serialize(
					JsonOutput.ToStatusJson(activeEntry, _timeProvider.GetLocalNow().DateTime)));
				return 0;
			}

			if (format == OutputFormat.Plain)
			{
				if (activeEntry != null)
				{
					var elapsedMinutes = (int)(_timeProvider.GetLocalNow().DateTime - activeEntry.StartTime).TotalMinutes;
					CliConsole.Data.WriteLine(PlainOutput.FormatEntry(activeEntry, elapsedMinutes));
				}

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

	public async Task<int> HandleListCommand(DateTime? date = null, OutputFormat format = OutputFormat.Table)
	{
		try
		{
			var targetDate = date ?? _timeProvider.GetLocalNow().Date;
			var entries = await _workEntryService.GetWorkEntriesByDateAsync(targetDate);

			if (format == OutputFormat.Json)
			{
				CliConsole.Data.WriteLine(JsonOutput.Serialize(JsonOutput.ToListJson(targetDate, entries)));
				return 0;
			}

			if (format == OutputFormat.Plain)
			{
				foreach (var entry in entries)
				{
					CliConsole.Data.WriteLine(PlainOutput.FormatEntry(entry));
				}

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

			var result = await _workEntryService.EditWorkEntryAsync(id, new WorkEntryChanges(ticketId, startTime, endTime, description), CancellationToken.None);

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
	public int HandleProvidersCommand(OutputFormat format = OutputFormat.Table)
	{
		try
		{
			var enabledIds = _pluginManager.EnabledWorklogUploadPlugins
				.Select(p => p.Metadata.Id)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var providers = _pluginManager.AllWorklogUploadPlugins
				.Select(p => (p.Metadata.Id, p.Metadata.Name, Enabled: enabledIds.Contains(p.Metadata.Id)))
				.OrderByDescending(p => p.Enabled)
				.ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (format == OutputFormat.Json)
			{
				CliConsole.Data.WriteLine(JsonOutput.Serialize(
					providers.Select(p => new JsonOutput.ProviderJson(p.Id, p.Name, p.Enabled)).ToList()));
				return 0;
			}

			if (format == OutputFormat.Plain)
			{
				foreach (var (id, name, enabled) in providers)
				{
					CliConsole.Data.WriteLine(PlainOutput.FormatProvider(id, name, enabled));
				}

				// Hints go to stderr here so they never end up among the records.
				if (providers.Count == 0)
				{
					CliConsole.Error.MarkupLine("[yellow]No worklog upload plugins installed[/]");
				}
				else if (!providers.Any(p => p.Enabled))
				{
					CliConsole.Error.MarkupLine("[yellow]No provider is enabled[/] — enable one in the desktop app's settings.");
				}

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
			// The submission refuses this as well, but by then the user has gone through the preview
			var enabledProviders = _submissionService.GetAvailableProviders().ToList();
			if (providerId == null && enabledProviders.Count > 1)
			{
				CliConsole.Error.MarkupLine("[red]✗ Error:[/] Several worklog providers are enabled, choose one with --provider=<id>:");
				foreach (var enabled in enabledProviders)
				{
					CliConsole.Error.MarkupLine($"  [cyan]{Markup.Escape(enabled.Id)}[/] ({Markup.Escape(enabled.Name)})");
				}

				return 1;
			}

			var targetDate = date ?? _timeProvider.GetLocalNow().Date;
			var period = isWeek ? SubmissionPeriod.WeekOf(targetDate) : SubmissionPeriod.Day(targetDate);

			var preview = await _submissionService.PreviewAsync(period, WorklogSubmissionMode.Timed, CancellationToken.None);

			// The preview holds one item per day, so it isn't empty just because nothing was done
			if (!preview.Any(day => day.Worklogs.Count != 0))
			{
				CliConsole.Out.MarkupLine(isWeek ? "[yellow]No completed entries to send for the week[/]" : "[yellow]No completed entries to send[/]");
				return 0;
			}

			if (isWeek)
			{
				WriteWeekPreview(preview);
			}
			else
			{
				WriteDayPreview(targetDate, preview[0]);
			}

			var provider = Markup.Escape(DescribeProvider(providerId));
			var question = isWeek ? $"\nSend all these entries to {provider}?" : $"\nSend these entries to {provider}?";
			if (!assumeYes && !CliConsole.Out.Confirm(question))
			{
				CliConsole.Out.MarkupLine("[yellow]Cancelled[/]");
				return 0;
			}

			var result = await _submissionService.SubmitAsync(period, providerId, CancellationToken.None);

			if (result.IsSuccess)
			{
				return ReportSubmission(result.Value, provider);
			}
			else
			{
				CliConsole.Error.MarkupLine($"[red]✗ Failed to send entries to {provider}:[/] {Markup.Escape(result.Error)}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			CliConsole.Error.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(ex.Message)}");
			return 1;
		}
	}

	/// <summary>
	/// Reports what the provider did with the entries. A call that went through can still have failed
	/// for some of them, and a script relying on the exit code has to learn about that.
	/// </summary>
	private static int ReportSubmission(SubmissionResult submission, string provider)
	{
		if (submission.SuccessfulEntries > 0 || !submission.HasFailures)
		{
			CliConsole.Out.MarkupLine($"[green]✓[/] Successfully sent {submission.SuccessfulEntries} entries to {provider}");
		}

		if (!submission.HasFailures)
		{
			return 0;
		}

		if (submission.FailedEntries > 0)
		{
			CliConsole.Error.MarkupLine($"[yellow]⚠[/] {submission.FailedEntries} entries failed");
		}

		foreach (var error in submission.Errors)
		{
			var day = error.Date is { } date ? $"{date:yyyy-MM-dd}: " : string.Empty;
			CliConsole.Error.MarkupLine($"  [red]-[/] {day}{Markup.Escape(error.ErrorMessage)}");
		}

		return 1;
	}

	/// <summary>
	/// Names the plugin a send goes to, for the prompt and the result. Without --provider the only
	/// enabled upload plugin is used, since more of them stop the command earlier. An id that matches
	/// nothing is shown as given; the submission then fails and says so.
	/// </summary>
	private string DescribeProvider(string? providerId)
	{
		var providers = _submissionService.GetAvailableProviders();
		var provider = providerId == null
			? providers.FirstOrDefault()
			: providers.FirstOrDefault(p => p.Id == providerId);

		return provider?.Name ?? providerId ?? "the worklog provider";
	}

	private static void WriteDayPreview(DateTime date, WorklogSubmissionDto day)
	{
		CliConsole.Out.MarkupLine($"[bold]Preview of entries to send for {date:yyyy-MM-dd}:[/]\n");
		CliConsole.Out.Write(WorklogTable(day.Worklogs));
	}

	private static void WriteWeekPreview(IReadOnlyList<WorklogSubmissionDto> days)
	{
		CliConsole.Out.MarkupLine($"[bold]Preview of entries to send for week {days[0].SubmissionDate:yyyy-MM-dd} to {days[^1].SubmissionDate:yyyy-MM-dd}:[/]\n");

		foreach (var day in days.Where(day => day.Worklogs.Count != 0))
		{
			CliConsole.Out.MarkupLine($"\n[bold cyan]{day.SubmissionDate:ddd yyyy-MM-dd}[/]");

			var table = WorklogTable(day.Worklogs);
			table.Border = TableBorder.Minimal;
			CliConsole.Out.Write(table);
		}

		var worklogs = days.SelectMany(day => day.Worklogs).ToList();
		var totalMinutes = worklogs.Sum(w => w.DurationMinutes);
		CliConsole.Out.MarkupLine($"\n[bold]Total:[/] {worklogs.Count} entries, {totalMinutes / 60}h {totalMinutes % 60}m");
	}

	private static Table WorklogTable(IEnumerable<WorklogDto> worklogs)
	{
		var table = new Table();
		table.AddColumn("Ticket");
		table.AddColumn("Start");
		table.AddColumn("End");
		table.AddColumn("Duration");

		foreach (var worklog in worklogs)
		{
			table.AddRow(
				string.IsNullOrWhiteSpace(worklog.TicketId) ? "[dim]N/A[/]" : Markup.Escape(worklog.TicketId),
				worklog.StartTime.ToString("HH:mm"),
				worklog.EndTime.ToString("HH:mm"),
				$"{worklog.DurationMinutes / 60}h {worklog.DurationMinutes % 60}m"
			);
		}

		return table;
	}
}
