using Spectre.Console;

bool plain = false;
bool interactive = false;
string? habitArg = null;
string? startArg = null;
string? stopArg = null;
string? resetArg = null;
string? goalHabitArg = null;
string? goalDurationArg = null;

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

    if (arg == "goal")
    {
        if (i + 2 >= args.Length)
        {
            PrintError("--goal requires a habit name/ID and a duration. Example: --goal YouTube 7d, or --goal YouTube clear");
            return 1;
        }
        goalHabitArg = args[++i];
        goalDurationArg = args[++i];
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

if (goalHabitArg is not null && goalDurationArg is not null)
{
    var habit = repo.GetHabitByNameOrId(goalHabitArg);
    if (habit is null)
    {
        PrintError($"Habit not found: {goalHabitArg}");
        PrintHabits();
        return 1;
    }

    if (goalDurationArg.Equals("clear", StringComparison.OrdinalIgnoreCase) || goalDurationArg.Equals("none", StringComparison.OrdinalIgnoreCase))
    {
        repo.SetGoal(habit.Id, null);
        PrintSuccess($"Cleared goal for: {habit.Description}");
        ShowReport();
        return 0;
    }

    if (!TryParseDuration(goalDurationArg, out var goal))
    {
        PrintError($"Invalid duration: {goalDurationArg}. Examples: 7d, 12h, 30m, 7d12h, 1:30:00");
        return 1;
    }
    repo.SetGoal(habit.Id, goal);
    PrintSuccess($"Set goal for {habit.Description}: {FormatDuration(goal)}");
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
                .AddChoices("Start New Attempt", "Stop Current Attempt", "Reset Current Attempt", "Add New Habit", "Set Goal", "Exit"));

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

            var habitChoices = habits.Select(h => $"{Markup.Escape(h.Description)} (ID: {h.Id})").ToList();
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

            var habitChoices = habits.Select(h => Markup.Escape(h.Description)).ToList();
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

            var habitChoices = habits.Select(h => Markup.Escape(h.Description)).ToList();
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
            continue;
        }

        if (choice == "Set Goal")
        {
            var habits = repo.GetHabits();
            if (habits.Count == 0)
            {
                PrintWarning("No habits defined. Add one first.");
                AnsiConsole.WriteLine();
                continue;
            }

            var habitChoices = habits.Select(h =>
            {
                var goalStr = h.Goal.HasValue ? $" — goal: {FormatDuration(h.Goal.Value)}" : "";
                return $"{Markup.Escape(h.Description)} (ID: {h.Id}){goalStr}";
            }).ToList();
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Select habit:[/]")
                    .PageSize(10)
                    .AddChoices(habitChoices));

            var idx = habitChoices.IndexOf(selected);
            var habit = habits[idx];

            var input = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold]Goal duration[/] (e.g. [green]7d[/], [green]12h[/], [green]30m[/], [green]1:30:00[/], or [green]clear[/]):")
                    .Validate(s =>
                    {
                        if (s.Equals("clear", StringComparison.OrdinalIgnoreCase) || s.Equals("none", StringComparison.OrdinalIgnoreCase))
                            return ValidationResult.Success();
                        return TryParseDuration(s, out _)
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Enter a valid duration like 7d, 12h, 30m, or 1:30:00");
                    }));

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase) || input.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                repo.SetGoal(habit.Id, null);
                PrintSuccess($"Cleared goal for: {habit.Description}");
            }
            else
            {
                TryParseDuration(input, out var goal);
                repo.SetGoal(habit.Id, goal);
                PrintSuccess($"Set goal for {habit.Description}: {FormatDuration(goal)}");
            }
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
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"ID",-4} {"Habit",-20} {"Current",-16} {"Goal",-20} {"Best",-16} {"Best Date",-12} {"Avg (Last 7)",-16}");
        Console.WriteLine(new string('-', 100));
        foreach (var r in reports)
        {
            var current = r.CurrentDuration.HasValue ? FormatDuration(r.CurrentDuration.Value) : "---";
            var goal = FormatGoalPlain(r.Goal, r.CurrentDuration);
            var best = r.BestDuration.HasValue ? FormatDuration(r.BestDuration.Value) : "---";
            var bestDate = r.BestDate.HasValue ? r.BestDate.Value.ToString("yyyy-MM-dd") : "---";
            var avg = r.RollingAverage.HasValue ? FormatDuration(r.RollingAverage.Value) : "---";
            Console.WriteLine($"{r.Id,-4} {r.Description,-20} {current,-16} {goal,-20} {best,-16} {bestDate,-12} {avg,-16}");
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
            .AddColumn(new TableColumn("[bold]Goal[/]").Centered())
            .AddColumn(new TableColumn("[bold]Best[/]").Centered())
            .AddColumn(new TableColumn("[bold]Best Date[/]").Centered())
            .AddColumn(new TableColumn("[bold]Avg (Last 7)[/]").Centered());

        foreach (var r in reports)
        {
            var current = FormatCurrentMarkup(r.CurrentDuration, r.Goal);
            var goal = FormatGoalMarkup(r.Goal, r.CurrentDuration);
            var best = r.BestDuration.HasValue
                ? $"[yellow]{FormatDuration(r.BestDuration.Value)}[/]"
                : "[dim]---[/]";
            var bestDate = r.BestDate.HasValue
                ? r.BestDate.Value.ToString("yyyy-MM-dd")
                : "[dim]---[/]";
            var avg = r.RollingAverage.HasValue
                ? $"[cyan]{FormatDuration(r.RollingAverage.Value)}[/]"
                : "[dim]---[/]";

            table.AddRow(r.Id.ToString(), Markup.Escape(r.Description), current, goal, best, bestDate, avg);
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

string FormatCurrentMarkup(TimeSpan? current, TimeSpan? goal)
{
    if (!current.HasValue) return "[dim]---[/]";
    var text = FormatDuration(current.Value);
    if (!goal.HasValue) return $"[green]{text}[/]";
    if (current.Value >= goal.Value) return $"[bold green]{text}[/]";
    var pct = current.Value.TotalSeconds / goal.Value.TotalSeconds;
    var color = pct switch
    {
        >= 0.75 => "green",
        >= 0.5 => "cyan",
        >= 0.25 => "yellow",
        _ => "red"
    };
    return $"[{color}]{text}[/]";
}

string FormatGoalMarkup(TimeSpan? goal, TimeSpan? current)
{
    if (!goal.HasValue) return "[dim]---[/]";
    var goalText = FormatDuration(goal.Value);
    if (!current.HasValue) return $"[blue]{goalText}[/]";
    if (current.Value >= goal.Value) return $"[bold green]{goalText} ✓[/]";
    var pct = (int)(current.Value.TotalSeconds / goal.Value.TotalSeconds * 100);
    return $"[blue]{goalText}[/] [dim]({pct}%)[/]";
}

string FormatGoalPlain(TimeSpan? goal, TimeSpan? current)
{
    if (!goal.HasValue) return "---";
    var goalText = FormatDuration(goal.Value);
    if (!current.HasValue) return goalText;
    if (current.Value >= goal.Value) return $"{goalText} MET";
    var pct = (int)(current.Value.TotalSeconds / goal.Value.TotalSeconds * 100);
    return $"{goalText} ({pct}%)";
}

// Parses durations like "7d", "12h", "30m", "7d12h30m", or standard TimeSpan ("1:30:00", "7.12:00:00")
bool TryParseDuration(string input, out TimeSpan duration)
{
    duration = TimeSpan.Zero;
    if (string.IsNullOrWhiteSpace(input)) return false;

    var trimmed = input.Trim();

    // Standard TimeSpan format
    if (TimeSpan.TryParse(trimmed, out duration) && duration > TimeSpan.Zero)
        return true;

    // Shortcut format: 7d12h30m
    var total = TimeSpan.Zero;
    var num = "";
    bool found = false;
    foreach (var c in trimmed.ToLowerInvariant())
    {
        if (char.IsDigit(c))
        {
            num += c;
        }
        else if (num.Length > 0 && (c == 'd' || c == 'h' || c == 'm'))
        {
            var n = int.Parse(num);
            total += c switch
            {
                'd' => TimeSpan.FromDays(n),
                'h' => TimeSpan.FromHours(n),
                'm' => TimeSpan.FromMinutes(n),
                _ => TimeSpan.Zero
            };
            num = "";
            found = true;
        }
        else if (!char.IsWhiteSpace(c))
        {
            return false;
        }
    }
    if (num.Length > 0) return false; // trailing digits without unit
    if (!found || total <= TimeSpan.Zero) return false;
    duration = total;
    return true;
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
        Console.WriteLine("  --habit <desc>             Create a new habit to track");
        Console.WriteLine("  --start <name|id>          Start a new abstinence attempt");
        Console.WriteLine("  --stop <name|id>           Stop the current attempt");
        Console.WriteLine("  --reset <name|id>          Reset the current attempt timer");
        Console.WriteLine("  --goal <name|id> <dur>     Set goal (e.g. 7d, 12h, 30m, 1:30:00, or 'clear')");
        Console.WriteLine("  --plain                    Plain text output (no colors)");
        Console.WriteLine("  --interactive              Launch interactive mode");
        Console.WriteLine("  --help, -h                 Show this help");
        Console.WriteLine();
        Console.WriteLine("No arguments shows the current report.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  abs");
        Console.WriteLine("  abs --habit Smoking");
        Console.WriteLine("  abs --start Smoking");
        Console.WriteLine("  abs --stop Smoking");
        Console.WriteLine("  abs --reset Smoking");
        Console.WriteLine("  abs --goal Smoking 7d");
        Console.WriteLine("  abs --goal Smoking clear");
        Console.WriteLine("  abs --interactive");
    }
    else
    {
        var panel = new Panel(
            new Rows(
                new Markup("[bold]Options:[/]"),
                new Markup("  [green]--habit <desc>[/]             Create a new habit to track"),
                new Markup("  [green]--start <name|id>[/]          Start a new abstinence attempt"),
                new Markup("  [green]--stop <name|id>[/]           Stop the current attempt"),
                new Markup("  [green]--reset <name|id>[/]          Reset the current attempt timer"),
                new Markup("  [green]--goal <name|id> <dur>[/]     Set goal (e.g. 7d, 12h, 30m, 1:30:00, or 'clear')"),
                new Markup("  [green]--plain[/]                    Plain text output (no colors)"),
                new Markup("  [green]--interactive[/]              Launch interactive mode"),
                new Markup("  [green]--help[/], [green]-h[/]                 Show this help"),
                new Markup(""),
                new Markup("[bold]Examples:[/]"),
                new Markup("  [dim]abs[/]"),
                new Markup("  [dim]abs --habit Smoking[/]"),
                new Markup("  [dim]abs --start Smoking[/]"),
                new Markup("  [dim]abs --stop Smoking[/]"),
                new Markup("  [dim]abs --reset Smoking[/]"),
                new Markup("  [dim]abs --goal Smoking 7d[/]"),
                new Markup("  [dim]abs --goal Smoking clear[/]"),
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
