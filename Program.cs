using Spectre.Console;

bool plain = false;
bool interactive = false;
string? habitArg = null;
string? startArg = null;
string? stopArg = null;
string? resetArg = null;

// Parse arguments
for (int i = 0; i < args.Length; i++)
{
    var arg = args[i].TrimStart('-').ToLowerInvariant();

    if (arg is "help" or "h" or "?")
    {
        PrintUsage();
        return 0;
    }

    if (arg == "plain")
    {
        plain = true;
        continue;
    }

    if (arg == "interactive")
    {
        interactive = true;
        continue;
    }

    if (arg == "habit")
    {
        if (i + 1 >= args.Length)
        {
            PrintError("--habit requires a description. Example: --habit Smoking");
            return 1;
        }
        habitArg = args[++i];
        continue;
    }

    if (arg == "start")
    {
        if (i + 1 >= args.Length)
        {
            PrintError("--start requires a habit name or ID. Example: --start Smoking");
            return 1;
        }
        startArg = args[++i];
        continue;
    }

    if (arg == "stop")
    {
        if (i + 1 >= args.Length)
        {
            PrintError("--stop requires a habit name or ID. Example: --stop Smoking");
            return 1;
        }
        stopArg = args[++i];
        continue;
    }

    if (arg == "reset")
    {
        if (i + 1 >= args.Length)
        {
            PrintError("--reset requires a habit name or ID. Example: --reset Smoking");
            return 1;
        }
        resetArg = args[++i];
        continue;
    }

    PrintError($"Unknown argument: {args[i]}");
    PrintUsage();
    return 1;
}

// Open database
var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", "abstain.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
using var repo = new AbstainRepository(dbPath);
repo.Initialize();

// Dispatch
if (habitArg is not null)
{
    var habit = repo.CreateHabit(habitArg);
    PrintSuccess($"Created habit: {habit.Description} (ID: {habit.Id})");
    ShowReport();
    return 0;
}

if (stopArg is not null)
{
    var habit = repo.GetHabitByNameOrId(stopArg);
    if (habit is null)
    {
        PrintError($"Habit not found: {stopArg}");
        PrintHabits();
        return 1;
    }
    var rows = repo.StopAttempt(habit.Id);
    if (rows == 0)
    {
        PrintWarning($"No active attempt found for: {habit.Description}");
        return 1;
    }
    PrintSuccess($"Stopped attempt for: {habit.Description}");
    ShowReport();
    return 0;
}

if (startArg is not null)
{
    var habit = repo.GetHabitByNameOrId(startArg);
    if (habit is null)
    {
        PrintError($"Habit not found: {startArg}");
        PrintHabits();
        return 1;
    }
    var active = repo.GetActiveEntry(habit.Id);
    if (active is not null)
    {
        PrintWarning($"Already tracking: {habit.Description}. Use --stop first or --reset to restart.");
        return 1;
    }
    repo.StartAttempt(habit.Id);
    PrintSuccess($"Started tracking: {habit.Description}");
    ShowReport();
    return 0;
}

if (resetArg is not null)
{
    var habit = repo.GetHabitByNameOrId(resetArg);
    if (habit is null)
    {
        PrintError($"Habit not found: {resetArg}");
        PrintHabits();
        return 1;
    }
    var rows = repo.ResetAttempt(habit.Id);
    if (rows == 0)
    {
        PrintWarning($"No active attempt found for: {habit.Description}");
        return 1;
    }
    PrintSuccess($"Reset timer for: {habit.Description}");
    ShowReport();
    return 0;
}

if (interactive)
{
    return RunInteractive();
}

// Default: show report
ShowReport();
return 0;

// ── Interactive mode ────────────────────────────────────────────────
int RunInteractive()
{
    while (true)
    {
        ShowReport();
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold blue]Abstain Tracker[/]")
                .AddChoices("Start New Attempt", "Stop Current Attempt", "Reset Current Attempt", "Add New Habit", "Exit"));

        if (choice == "Exit")
            return 0;

        if (choice == "Add New Habit")
        {
            var desc = AnsiConsole.Ask<string>("[bold]Habit description:[/]");
            var habit = repo.CreateHabit(desc);
            PrintSuccess($"Created habit: {habit.Description} (ID: {habit.Id})");
            AnsiConsole.WriteLine();
            continue;
        }

        if (choice == "Start New Attempt")
        {
            var habits = repo.GetHabits();
            if (habits.Count == 0)
            {
                PrintWarning("No habits defined. Add one first.");
                AnsiConsole.WriteLine();
                continue;
            }

            var habitChoices = habits.Select(h => $"{h.Description} (ID: {h.Id})").ToList();
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Select habit:[/]")
                    .PageSize(10)
                    .AddChoices(habitChoices));

            var idx = habitChoices.IndexOf(selected);
            var habit = habits[idx];

            var active = repo.GetActiveEntry(habit.Id);
            if (active is not null)
            {
                PrintWarning($"Already tracking: {habit.Description}. Stop or reset first.");
                AnsiConsole.WriteLine();
                continue;
            }

            repo.StartAttempt(habit.Id);
            PrintSuccess($"Started tracking: {habit.Description}");
            AnsiConsole.WriteLine();
            continue;
        }

        if (choice == "Stop Current Attempt")
        {
            var habits = repo.GetHabits().Where(h => repo.GetActiveEntry(h.Id) is not null).ToList();
            if (habits.Count == 0)
            {
                PrintWarning("No active attempts to stop.");
                AnsiConsole.WriteLine();
                continue;
            }

            var habitChoices = habits.Select(h => h.Description).ToList();
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Select habit to stop:[/]")
                    .PageSize(10)
                    .AddChoices(habitChoices));

            var idx = habitChoices.IndexOf(selected);
            var habit = habits[idx];
            repo.StopAttempt(habit.Id);
            PrintSuccess($"Stopped attempt for: {habit.Description}");
            AnsiConsole.WriteLine();
            continue;
        }

        if (choice == "Reset Current Attempt")
        {
            var habits = repo.GetHabits().Where(h => repo.GetActiveEntry(h.Id) is not null).ToList();
            if (habits.Count == 0)
            {
                PrintWarning("No active attempts to reset.");
                AnsiConsole.WriteLine();
                continue;
            }

            var habitChoices = habits.Select(h => h.Description).ToList();
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Select habit to reset:[/]")
                    .PageSize(10)
                    .AddChoices(habitChoices));

            var idx = habitChoices.IndexOf(selected);
            var habit = habits[idx];
            repo.ResetAttempt(habit.Id);
            PrintSuccess($"Reset timer for: {habit.Description}");
            AnsiConsole.WriteLine();
        }
    }
}

// ── Report display ──────────────────────────────────────────────────
void ShowReport()
{
    var reports = repo.GetReport();

    if (reports.Count == 0)
    {
        if (plain)
            Console.WriteLine("No habits tracked yet. Use --habit to add one.");
        else
            AnsiConsole.MarkupLine("[dim]No habits tracked yet. Use --habit to add one.[/]");
        return;
    }

    if (plain)
    {
        Console.WriteLine("Abstain Tracker");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"{"ID",-4} {"Habit",-20} {"Current",-16} {"Best",-16} {"Best Date",-14} {"Avg (Last 7)",-16}");
        Console.WriteLine(new string('-', 84));
        foreach (var r in reports)
        {
            var current = r.CurrentDuration.HasValue ? FormatDuration(r.CurrentDuration.Value) : "---";
            var best = r.BestDuration.HasValue ? FormatDuration(r.BestDuration.Value) : "---";
            var bestDate = r.BestDate.HasValue ? r.BestDate.Value.ToString("yyyy-MM-dd") : "---";
            var avg = r.RollingAverage.HasValue ? FormatDuration(r.RollingAverage.Value) : "---";
            Console.WriteLine($"{r.Id,-4} {r.Description,-20} {current,-16} {best,-16} {bestDate,-14} {avg,-16}");
        }
    }
    else
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Abstain Tracker[/]")
            .AddColumn(new TableColumn("[bold]ID[/]").Centered())
            .AddColumn("Habit")
            .AddColumn(new TableColumn("[bold]Current[/]").Centered())
            .AddColumn(new TableColumn("[bold]Best[/]").Centered())
            .AddColumn(new TableColumn("[bold]Best Date[/]").Centered())
            .AddColumn(new TableColumn("[bold]Avg (Last 7)[/]").Centered());

        foreach (var r in reports)
        {
            var current = r.CurrentDuration.HasValue
                ? $"[green]{FormatDuration(r.CurrentDuration.Value)}[/]"
                : "[dim]---[/]";
            var best = r.BestDuration.HasValue
                ? $"[yellow]{FormatDuration(r.BestDuration.Value)}[/]"
                : "[dim]---[/]";
            var bestDate = r.BestDate.HasValue
                ? r.BestDate.Value.ToString("yyyy-MM-dd")
                : "[dim]---[/]";
            var avg = r.RollingAverage.HasValue
                ? $"[cyan]{FormatDuration(r.RollingAverage.Value)}[/]"
                : "[dim]---[/]";

            table.AddRow(r.Id.ToString(), Markup.Escape(r.Description), current, best, bestDate, avg);
        }

        AnsiConsole.Write(table);
    }
}

string FormatDuration(TimeSpan duration)
{
    if (duration.TotalDays >= 1)
        return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes:D2}m";
    return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m";
}

// ── Helpers ──────────────────────────────────────────────────────────
void PrintHabits()
{
    var habits = repo.GetHabits();
    if (habits.Count == 0)
    {
        if (plain)
            Console.WriteLine("No habits defined. Use --habit to add one.");
        else
            AnsiConsole.MarkupLine("[dim]No habits defined. Use --habit to add one.[/]");
        return;
    }

    if (plain)
    {
        Console.WriteLine("Available habits:");
        foreach (var h in habits)
            Console.WriteLine($"  {h.Id}: {h.Description}");
    }
    else
    {
        AnsiConsole.MarkupLine("[bold]Available habits:[/]");
        foreach (var h in habits)
            AnsiConsole.MarkupLine($"  [green]{h.Id}[/]: {Markup.Escape(h.Description)}");
    }
}

void PrintUsage()
{
    if (plain)
    {
        Console.WriteLine("abstain - Track how long you've abstained from bad habits");
        Console.WriteLine();
        Console.WriteLine("Usage: abs [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --habit <desc>      Create a new habit to track");
        Console.WriteLine("  --start <name|id>   Start a new abstinence attempt");
        Console.WriteLine("  --stop <name|id>    Stop the current attempt");
        Console.WriteLine("  --reset <name|id>   Reset the current attempt timer");
        Console.WriteLine("  --plain             Plain text output (no colors)");
        Console.WriteLine("  --interactive       Launch interactive mode");
        Console.WriteLine("  --help, -h          Show this help");
        Console.WriteLine();
        Console.WriteLine("No arguments shows the current report.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  abs");
        Console.WriteLine("  abs --habit Smoking");
        Console.WriteLine("  abs --start Smoking");
        Console.WriteLine("  abs --stop Smoking");
        Console.WriteLine("  abs --reset Smoking");
        Console.WriteLine("  abs --interactive");
    }
    else
    {
        var panel = new Panel(
            new Rows(
                new Markup("[bold]Options:[/]"),
                new Markup("  [green]--habit <desc>[/]      Create a new habit to track"),
                new Markup("  [green]--start <name|id>[/]   Start a new abstinence attempt"),
                new Markup("  [green]--stop <name|id>[/]    Stop the current attempt"),
                new Markup("  [green]--reset <name|id>[/]   Reset the current attempt timer"),
                new Markup("  [green]--plain[/]             Plain text output (no colors)"),
                new Markup("  [green]--interactive[/]       Launch interactive mode"),
                new Markup("  [green]--help[/], [green]-h[/]          Show this help"),
                new Markup(""),
                new Markup("[bold]Examples:[/]"),
                new Markup("  [dim]abs[/]"),
                new Markup("  [dim]abs --habit Smoking[/]"),
                new Markup("  [dim]abs --start Smoking[/]"),
                new Markup("  [dim]abs --stop Smoking[/]"),
                new Markup("  [dim]abs --reset Smoking[/]"),
                new Markup("  [dim]abs --interactive[/]"),
                new Markup(""),
                new Markup("[dim]No arguments shows the current report.[/]")
            ))
            .Header("[bold blue]abstain[/]")
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);

        AnsiConsole.Write(panel);
    }
}

void PrintError(string message)
{
    if (plain)
        Console.Error.WriteLine($"ERROR: {message}");
    else
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
}

void PrintWarning(string message)
{
    if (plain)
        Console.Error.WriteLine($"WARNING: {message}");
    else
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
}

void PrintSuccess(string message)
{
    if (plain)
        Console.WriteLine(message);
    else
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
}
