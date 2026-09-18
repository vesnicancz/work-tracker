// CommandHandler writes through process-wide console statics (AnsiConsole.Console, CliConsole),
// so every test in this assembly shares one collection and never runs concurrently.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
