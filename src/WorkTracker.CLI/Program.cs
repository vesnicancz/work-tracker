using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console;
using WorkTracker.Application;
using WorkTracker.Application.Settings;
using WorkTracker.CLI.Commands;
using WorkTracker.CLI.Output;
using WorkTracker.Infrastructure;
using WorkTracker.Infrastructure.Data;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Configuration
	.SetBasePath(Directory.GetCurrentDirectory())
	.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
	.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
	.AddEnvironmentVariables();

// Logging
builder.Services.AddSerilog(loggerConfiguration =>
{
	loggerConfiguration
		.MinimumLevel.Information()
		.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
		.MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
		.WriteTo.Console(
			restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning,
			standardErrorFromLevel: Serilog.Events.LogEventLevel.Warning)
		.WriteTo.File(WorkTrackerPaths.CliLogFilePath,
			rollingInterval: RollingInterval.Day,
			retainedFileCountLimit: 14,
			outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
});

// Services
IHost host;
try
{
	builder.Services.AddInfrastructure(builder.Configuration);
	// Note: IWorkEntryService and IWorklogSubmissionService are registered in Infrastructure layer
	builder.Services.AddTransient<CommandHandler>();

	host = builder.Build();

	// Initialize database
	await WorkTracker.Infrastructure.DependencyInjection.InitializeDatabaseAsync(host.Services);

	// Initialize plugins. Enabled state and configuration live in the user settings file the GUI
	// writes — without them every plugin stays disabled and "send" can never resolve a provider.
	var pluginSettings = await host.Services
		.GetRequiredService<IPluginSettingsReader>()
		.ReadAsync();

	await WorkTracker.Infrastructure.DependencyInjection.InitializePluginsAsync(
		host.Services,
		builder.Configuration,
		pluginSettings.EnabledPlugins,
		pluginSettings.PluginConfigurations);
}
catch (DatabaseUnavailableException ex)
{
	// Nothing is logged here — this can fail before the host (and Serilog) exists. Unlike the
	// GUI there is nothing to retry: a one-shot command cannot wait for the drive to appear.
	CliConsole.Error.MarkupLine($"[red]Error:[/] Database is not available: {Markup.Escape(ex.DatabasePath)}");
	CliConsole.Error.MarkupLine("If it is on a removable drive, connect the drive and run the command again.");
	return 1;
}

args = CliArgumentParser.TakeSwitch(args, out var jsonOutput, "--json");
args = CliArgumentParser.TakeSwitch(args, out var assumeYes, "--yes", "-y");
args = CliArgumentParser.TakeOption(args, out var providerId, "--provider");

// Parse command line arguments
if (args.Length == 0)
{
	ShowHelp();
	return 0;
}

using var scope = host.Services.CreateScope();
var commandHandler = scope.ServiceProvider.GetRequiredService<CommandHandler>();

var command = args[0].ToLower();

try
{
	return command switch
	{
		"start" => await HandleStartCommand(commandHandler, args),
		"stop" => await HandleStopCommand(commandHandler, args),
		"status" => await commandHandler.HandleStatusCommand(jsonOutput),
		"list" => await HandleListCommand(commandHandler, args, jsonOutput),
		"edit" => await HandleEditCommand(commandHandler, args),
		"delete" => await HandleDeleteCommand(commandHandler, args),
		"send" => await HandleSendCommand(commandHandler, args, assumeYes, providerId),
		"providers" => commandHandler.HandleProvidersCommand(jsonOutput),
		"version" or "--version" or "-v" => ShowVersion(),
		"help" or "--help" or "-h" => ShowHelp(),
		_ => ShowUnknownCommand(command)
	};
}
catch (Exception ex)
{
	CliConsole.Error.WriteException(ex);
	return 1;
}

static async Task<int> HandleStartCommand(CommandHandler handler, string[] args)
{
	if (args.Length < 2)
	{
		CliConsole.Error.MarkupLine("[red]Error:[/] At least a ticket ID or description is required");
		CliConsole.Error.MarkupLine(Markup.Escape("Usage: worklog start [ticket-id] [description] [start-time]"));
		CliConsole.Error.MarkupLine("       worklog start PROJ-123 Working on authentication");
		CliConsole.Error.MarkupLine("       worklog start \"Working on something\" 09:00");
		return 1;
	}

	// Parse input to extract Jira code, description, and start time
	var (ticketId, description, startTime) = CliArgumentParser.ParseStartCommandInput(args);

	if (string.IsNullOrWhiteSpace(ticketId) && string.IsNullOrWhiteSpace(description))
	{
		CliConsole.Error.MarkupLine("[red]Error:[/] At least a ticket ID or description is required");
		return 1;
	}

	return await handler.HandleStartCommand(ticketId, startTime, description);
}

static async Task<int> HandleStopCommand(CommandHandler handler, string[] args)
{
	DateTime? endTime = null;

	if (args.Length >= 2)
	{
		endTime = CliArgumentParser.ParseDateTime(args[1]);
		if (endTime == null)
		{
			CliConsole.Error.MarkupLine("[red]Error:[/] Invalid date/time format");
			CliConsole.Error.MarkupLine("Supported formats: HH:mm, HH:mm:ss, yyyy-MM-dd HH:mm");
			return 1;
		}
	}

	return await handler.HandleStopCommand(endTime);
}

static async Task<int> HandleListCommand(CommandHandler handler, string[] args, bool json)
{
	DateTime? date = null;

	if (args.Length >= 2)
	{
		if (DateTime.TryParse(args[1], out var parsedDate))
		{
			date = parsedDate;
		}
		else
		{
			CliConsole.Error.MarkupLine("[red]Error:[/] Invalid date format");
			return 1;
		}
	}

	return await handler.HandleListCommand(date, json);
}

static async Task<int> HandleEditCommand(CommandHandler handler, string[] args)
{
	if (args.Length < 2)
	{
		CliConsole.Error.MarkupLine("[red]Error:[/] Entry ID is required");
		CliConsole.Error.MarkupLine(Markup.Escape("Usage: worklog edit <id> [--ticket=<ticket>] [--start=<time>] [--end=<time>] [--desc=<description>]"));
		return 1;
	}

	if (!int.TryParse(args[1], out var id))
	{
		CliConsole.Error.MarkupLine("[red]Error:[/] Invalid entry ID");
		return 1;
	}

	var options = CliArgumentParser.ParseEditOptions(args, out var invalidField);
	if (options == null)
	{
		CliConsole.Error.MarkupLine($"[red]Error:[/] Invalid {invalidField} time format");
		CliConsole.Error.MarkupLine("Supported formats: HH:mm, HH:mm:ss, yyyy-MM-dd HH:mm");
		return 1;
	}

	return await handler.HandleEditCommand(id, options.TicketId, options.StartTime, options.EndTime, options.Description);
}

static async Task<int> HandleDeleteCommand(CommandHandler handler, string[] args)
{
	if (args.Length < 2)
	{
		CliConsole.Error.MarkupLine("[red]Error:[/] Entry ID is required");
		CliConsole.Error.MarkupLine("Usage: worklog delete <id>");
		return 1;
	}

	if (!int.TryParse(args[1], out var id))
	{
		CliConsole.Error.MarkupLine("[red]Error:[/] Invalid entry ID");
		return 1;
	}

	return await handler.HandleDeleteCommand(id);
}

static async Task<int> HandleSendCommand(CommandHandler handler, string[] args, bool assumeYes, string? providerId)
{
	if (!CliArgumentParser.TryParseSendArguments(args, out var date, out var isWeek))
	{
		if (isWeek)
		{
			CliConsole.Error.MarkupLine("[red]Error:[/] Invalid date format");
		}
		else
		{
			CliConsole.Error.MarkupLine("[red]Error:[/] Invalid date format or unknown parameter");
			CliConsole.Error.MarkupLine(Markup.Escape("Usage: worklog send [week] [date]"));
		}

		return 1;
	}

	return await handler.HandleSendCommand(date, isWeek, assumeYes, providerId);
}

static int ShowVersion()
{
	CliConsole.Out.MarkupLine($"[bold]WorkTracker CLI[/] {WorkTracker.Application.AppInfo.Version}");
	return 0;
}

static int ShowHelp()
{
	var panel = new Panel(
		new Markup(@"[bold]WorkTracker CLI[/] - Time tracking for developers

[yellow]COMMANDS:[/]

  [cyan]start[/] [[ticket-id]] [[description]] [[start-time]]
	Start working on a task (with optional Jira ticket code and description)
	Jira code format: PROJECT-123 (automatically detected at the beginning)
	Example: worklog start PROJ-123
	Example: worklog start PROJ-123 Working on authentication
	Example: worklog start PROJ-123 Bug fix 09:00
	Example: worklog start ""Working on documentation""
	Example: worklog start ""Working on documentation"" ""2025-10-30 09:00""

  [cyan]stop[/] [[end-time]]
	Stop the active work entry
	Example: worklog stop
	Example: worklog stop 17:30
	Example: worklog stop ""2025-10-30 17:30""

  [cyan]status[/] [[--json]]
	Show the currently active work entry
	Example: worklog status
	Example: worklog status --json

  [cyan]list[/] [[date]] [[--json]]
	List work entries for a specific date (default: today)
	Example: worklog list
	Example: worklog list 2025-10-30
	Example: worklog list --json

  [cyan]edit[/] <id> [[options]]
	Edit an existing work entry
	Options:
	  --ticket=<ticket>      Change Jira ticket ID (optional)
	  --start=<time>         Change start time
	  --end=<time>           Change end time
	  --desc=<description>   Set or update description
	Example: worklog edit 5 --ticket=PROJ-124 --end=17:30
	Example: worklog edit 5 --desc=""Updated description""
	Example: worklog edit 5 --start=""2025-10-30 09:00"" --end=""2025-10-30 17:30""

  [cyan]delete[/] <id>
	Delete a work entry
	Example: worklog delete 5

  [cyan]providers[/]
	List installed worklog upload plugins and whether they are enabled
	Example: worklog providers
	Example: worklog providers --json

  [cyan]send[/] [[week]] [[date]] [[--yes]] [[--provider=<id>]]
	Send work entries to the enabled worklog plugin (default: today)
	Example: worklog send                    Send today's entries
	Example: worklog send 2025-10-30         Send specific day
	Example: worklog send week               Send current week
	Example: worklog send week 2025-10-30    Send week containing date
	Example: worklog send week --yes         Skip the confirmation prompt
	Example: worklog send --provider=tempo.worklog
	                                         Send via a specific provider

  [cyan]help[/]
	Show this help message

[yellow]SCRIPTING:[/]

	--json          Print machine-readable JSON on stdout (list, status, providers)
	--yes/-y        Skip the confirmation prompt (send)
	--provider=<id> Pick a worklog provider (see: worklog providers)

	Results go to stdout, errors and warnings to stderr; exit code is 0 on
	success and 1 on failure.
"))
	{
		Header = new PanelHeader("[green]WorkTracker Help[/]"),
		Border = BoxBorder.Rounded
	};

	CliConsole.Out.Write(panel);
	return 0;
}

static int ShowUnknownCommand(string command)
{
	CliConsole.Error.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(command)}");
	CliConsole.Error.MarkupLine("Run [cyan]worklog help[/] to see available commands");
	return 1;
}