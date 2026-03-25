using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using WorkTracker.CLI.Commands;
using WorkTracker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Configuration
	.SetBasePath(Directory.GetCurrentDirectory())
	.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
	.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
	.AddEnvironmentVariables();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Services
builder.Services.AddInfrastructure(builder.Configuration);
// Note: IWorkEntryService and IWorklogSubmissionService are registered in Infrastructure layer
builder.Services.AddTransient<CommandHandler>();

var host = builder.Build();

// Initialize database
await DependencyInjection.InitializeDatabaseAsync(host.Services);

// Initialize plugins (loads embedded + external plugins, initializes all with configuration)
await DependencyInjection.InitializePluginsAsync(host.Services, builder.Configuration);

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
		"status" => await commandHandler.HandleStatusCommand(),
		"list" => await HandleListCommand(commandHandler, args),
		"edit" => await HandleEditCommand(commandHandler, args),
		"delete" => await HandleDeleteCommand(commandHandler, args),
		"send" => await HandleSendCommand(commandHandler, args),
		"version" or "--version" or "-v" => ShowVersion(),
		"help" or "--help" or "-h" => ShowHelp(),
		_ => ShowUnknownCommand(command)
	};
}
catch (Exception ex)
{
	AnsiConsole.WriteException(ex);
	return 1;
}

static DateTime? ParseDateTime(string input)
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

static async Task<int> HandleStartCommand(CommandHandler handler, string[] args)
{
	if (args.Length < 2)
	{
		AnsiConsole.MarkupLine("[red]Error:[/] At least a ticket ID or description is required");
		AnsiConsole.MarkupLine("Usage: worklog start [ticket-id] [description] [start-time]");
		AnsiConsole.MarkupLine("       worklog start PROJ-123 Working on authentication");
		AnsiConsole.MarkupLine("       worklog start \"Working on something\" 09:00");
		return 1;
	}

	// Parse input to extract Jira code, description, and start time
	var (ticketId, description, startTime) = ParseStartCommandInput(args);

	if (string.IsNullOrWhiteSpace(ticketId) && string.IsNullOrWhiteSpace(description))
	{
		AnsiConsole.MarkupLine("[red]Error:[/] At least a ticket ID or description is required");
		return 1;
	}

	return await handler.HandleStartCommand(ticketId, startTime, description);
}

static (string? ticketId, string? description, DateTime? startTime) ParseStartCommandInput(string[] args)
{
	var jiraPattern = WorkTracker.Application.Common.JiraPatterns.TicketId();
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
		// Remove the Jira code from the input
		input = input.Substring(ticketId.Length).TrimStart();
	}

	// Now parse the remaining input for description and/or start time
	if (!string.IsNullOrWhiteSpace(input))
	{
		// Split by space to look for time components
		var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		// Try to find if the last part(s) could be a time
		DateTime? parsedTime = null;
		int timePartIndex = -1;

		// Check last part first (most common case: "description HH:mm")
		if (parts.Length > 0)
		{
			parsedTime = ParseDateTime(parts[^1]);
			if (parsedTime.HasValue)
			{
				timePartIndex = parts.Length - 1;
				startTime = parsedTime;
			}
		}

		// Check last 2 parts (case: "description yyyy-MM-dd HH:mm")
		if (!parsedTime.HasValue && parts.Length > 1)
		{
			var lastTwoParts = $"{parts[^2]} {parts[^1]}";
			parsedTime = ParseDateTime(lastTwoParts);
			if (parsedTime.HasValue)
			{
				timePartIndex = parts.Length - 2;
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
			// No time found, everything is description
			description = input;
		}
	}

	return (ticketId, description, startTime);
}

static async Task<int> HandleStopCommand(CommandHandler handler, string[] args)
{
	DateTime? endTime = null;

	if (args.Length >= 2)
	{
		endTime = ParseDateTime(args[1]);
		if (endTime == null)
		{
			AnsiConsole.MarkupLine("[red]Error:[/] Invalid date/time format");
			AnsiConsole.MarkupLine("Supported formats: HH:mm, HH:mm:ss, yyyy-MM-dd HH:mm");
			return 1;
		}
	}

	return await handler.HandleStopCommand(endTime);
}

static async Task<int> HandleListCommand(CommandHandler handler, string[] args)
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
			AnsiConsole.MarkupLine("[red]Error:[/] Invalid date format");
			return 1;
		}
	}

	return await handler.HandleListCommand(date);
}

static async Task<int> HandleEditCommand(CommandHandler handler, string[] args)
{
	if (args.Length < 2)
	{
		AnsiConsole.MarkupLine("[red]Error:[/] Entry ID is required");
		AnsiConsole.MarkupLine("Usage: worklog edit <id> [--ticket=<ticket>] [--start=<time>] [--end=<time>] [--desc=<description>]");
		return 1;
	}

	if (!int.TryParse(args[1], out var id))
	{
		AnsiConsole.MarkupLine("[red]Error:[/] Invalid entry ID");
		return 1;
	}

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
			var timeStr = arg.Substring("--start=".Length);
			startTime = ParseDateTime(timeStr);
			if (startTime == null)
			{
				AnsiConsole.MarkupLine("[red]Error:[/] Invalid start time format");
				AnsiConsole.MarkupLine("Supported formats: HH:mm, HH:mm:ss, yyyy-MM-dd HH:mm");
				return 1;
			}
		}
		else if (arg.StartsWith("--end="))
		{
			var timeStr = arg.Substring("--end=".Length);
			endTime = ParseDateTime(timeStr);
			if (endTime == null)
			{
				AnsiConsole.MarkupLine("[red]Error:[/] Invalid end time format");
				AnsiConsole.MarkupLine("Supported formats: HH:mm, HH:mm:ss, yyyy-MM-dd HH:mm");
				return 1;
			}
		}
		else if (arg.StartsWith("--desc="))
		{
			description = arg.Substring("--desc=".Length);
		}
	}

	return await handler.HandleEditCommand(id, ticketId, startTime, endTime, description);
}

static async Task<int> HandleDeleteCommand(CommandHandler handler, string[] args)
{
	if (args.Length < 2)
	{
		AnsiConsole.MarkupLine("[red]Error:[/] Entry ID is required");
		AnsiConsole.MarkupLine("Usage: worklog delete <id>");
		return 1;
	}

	if (!int.TryParse(args[1], out var id))
	{
		AnsiConsole.MarkupLine("[red]Error:[/] Invalid entry ID");
		return 1;
	}

	return await handler.HandleDeleteCommand(id);
}

static async Task<int> HandleSendCommand(CommandHandler handler, string[] args)
{
	DateTime? date = null;
	bool isWeek = false;

	// Check for "week" parameter
	if (args.Length >= 2 && args[1].Equals("week", StringComparison.OrdinalIgnoreCase))
	{
		isWeek = true;

		// Check if there's a date after "week"
		if (args.Length >= 3)
		{
			if (DateTime.TryParse(args[2], out var parsedDate))
			{
				date = parsedDate;
			}
			else
			{
				AnsiConsole.MarkupLine("[red]Error:[/] Invalid date format");
				return 1;
			}
		}
	}
	else if (args.Length >= 2)
	{
		// No "week" parameter, try to parse as date
		if (DateTime.TryParse(args[1], out var parsedDate))
		{
			date = parsedDate;
		}
		else
		{
			AnsiConsole.MarkupLine("[red]Error:[/] Invalid date format or unknown parameter");
			AnsiConsole.MarkupLine("Usage: worklog send [week] [date]");
			return 1;
		}
	}

	return await handler.HandleSendCommand(date, isWeek);
}

static int ShowVersion()
{
	AnsiConsole.MarkupLine($"[bold]WorkTracker CLI[/] {WorkTracker.Application.AppInfo.Version}");
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

  [cyan]status[/]
	Show the currently active work entry
	Example: worklog status

  [cyan]list[/] [[date]]
	List work entries for a specific date (default: today)
	Example: worklog list
	Example: worklog list 2025-10-30

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

  [cyan]send[/] [[week]] [[date]]
	Send work entries to Tempo (default: today)
	Example: worklog send                    Send today's entries
	Example: worklog send 2025-10-30         Send specific day
	Example: worklog send week               Send current week
	Example: worklog send week 2025-10-30    Send week containing date

  [cyan]help[/]
	Show this help message
"))
	{
		Header = new PanelHeader("[green]WorkTracker Help[/]"),
		Border = BoxBorder.Rounded
	};

	AnsiConsole.Write(panel);
	return 0;
}

static int ShowUnknownCommand(string command)
{
	AnsiConsole.MarkupLine($"[red]Unknown command:[/] {command}");
	AnsiConsole.MarkupLine("Run [cyan]worklog help[/] to see available commands");
	return 1;
}