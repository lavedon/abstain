using Microsoft.Data.Sqlite;

record Habit(int Id, string Description);
record LogEntry(int Id, int HabitId, DateTime Start, DateTime? End);
record HabitReport(string Description, TimeSpan? CurrentDuration, TimeSpan? BestDuration, DateTime? BestDate, TimeSpan? RollingAverage);

class AbstainRepository : IDisposable
{
    private readonly SqliteConnection _connection;

    public AbstainRepository(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
    }

    public void Initialize()
    {
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        using var createHabits = _connection.CreateCommand();
        createHabits.CommandText = """
            CREATE TABLE IF NOT EXISTS Habits (
                Id INTEGER PRIMARY KEY,
                Description TEXT NOT NULL
            );
            """;
        createHabits.ExecuteNonQuery();

        using var createLog = _connection.CreateCommand();
        createLog.CommandText = """
            CREATE TABLE IF NOT EXISTS Log (
                Id INTEGER PRIMARY KEY,
                Habit INTEGER NOT NULL,
                Start TEXT NOT NULL,
                End TEXT,
                FOREIGN KEY (Habit) REFERENCES Habits(Id)
            );
            """;
        createLog.ExecuteNonQuery();
    }

    public List<Habit> GetHabits()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Description FROM Habits ORDER BY Id;";

        var habits = new List<Habit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            habits.Add(new Habit(reader.GetInt32(0), reader.GetString(1)));
        }
        return habits;
    }

    public Habit? GetHabitByNameOrId(string input)
    {
        using var cmd = _connection.CreateCommand();

        if (int.TryParse(input, out var id))
        {
            cmd.CommandText = "SELECT Id, Description FROM Habits WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
        }
        else
        {
            cmd.CommandText = "SELECT Id, Description FROM Habits WHERE Description = @desc COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@desc", input);
        }

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new Habit(reader.GetInt32(0), reader.GetString(1));
        }
        return null;
    }

    public Habit CreateHabit(string description)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Habits (Description) VALUES (@desc);";
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.ExecuteNonQuery();

        using var idCmd = _connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid();";
        var id = Convert.ToInt32(idCmd.ExecuteScalar());
        return new Habit(id, description);
    }

    public void StartAttempt(int habitId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Log (Habit, Start) VALUES (@habitId, @start);";
        cmd.Parameters.AddWithValue("@habitId", habitId);
        cmd.Parameters.AddWithValue("@start", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public int StopAttempt(int habitId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Log SET End = @end
            WHERE Id = (
                SELECT Id FROM Log
                WHERE Habit = @habitId AND End IS NULL
                ORDER BY Id DESC LIMIT 1
            );
            """;
        cmd.Parameters.AddWithValue("@end", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@habitId", habitId);
        return cmd.ExecuteNonQuery();
    }

    public int ResetAttempt(int habitId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Log SET Start = @now
            WHERE Id = (
                SELECT Id FROM Log
                WHERE Habit = @habitId AND End IS NULL
                ORDER BY Id DESC LIMIT 1
            );
            """;
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@habitId", habitId);
        return cmd.ExecuteNonQuery();
    }

    public LogEntry? GetActiveEntry(int habitId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Habit, Start, End FROM Log
            WHERE Habit = @habitId AND End IS NULL
            ORDER BY Id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@habitId", habitId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new LogEntry(
                reader.GetInt32(0),
                reader.GetInt32(1),
                DateTime.Parse(reader.GetString(2)),
                null);
        }
        return null;
    }

    public List<LogEntry> GetCompletedAttempts(int habitId, int count)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Habit, Start, End FROM Log
            WHERE Habit = @habitId AND End IS NOT NULL
            ORDER BY Id DESC LIMIT @count;
            """;
        cmd.Parameters.AddWithValue("@habitId", habitId);
        cmd.Parameters.AddWithValue("@count", count);

        var entries = new List<LogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new LogEntry(
                reader.GetInt32(0),
                reader.GetInt32(1),
                DateTime.Parse(reader.GetString(2)),
                DateTime.Parse(reader.GetString(3))));
        }
        return entries;
    }

    public LogEntry? GetBestAttempt(int habitId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Habit, Start, End FROM Log
            WHERE Habit = @habitId AND End IS NOT NULL
            ORDER BY julianday(End) - julianday(Start) DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@habitId", habitId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new LogEntry(
                reader.GetInt32(0),
                reader.GetInt32(1),
                DateTime.Parse(reader.GetString(2)),
                DateTime.Parse(reader.GetString(3)));
        }
        return null;
    }

    public List<HabitReport> GetReport()
    {
        var habits = GetHabits();
        var reports = new List<HabitReport>();

        foreach (var habit in habits)
        {
            var active = GetActiveEntry(habit.Id);
            TimeSpan? currentDuration = active is not null
                ? DateTime.Now - active.Start
                : null;

            var best = GetBestAttempt(habit.Id);
            TimeSpan? bestDuration = best is not null
                ? best.End!.Value - best.Start
                : null;
            DateTime? bestDate = best?.Start;

            var completed = GetCompletedAttempts(habit.Id, 7);
            TimeSpan? rollingAvg = CalculateRollingAverage(completed);

            reports.Add(new HabitReport(
                habit.Description,
                currentDuration,
                bestDuration,
                bestDate,
                rollingAvg));
        }

        return reports;
    }

    private static TimeSpan? CalculateRollingAverage(List<LogEntry> completedAttempts)
    {
        if (completedAttempts.Count == 0)
            return null;

        var totalTicks = completedAttempts.Sum(e => (e.End!.Value - e.Start).Ticks);
        return TimeSpan.FromTicks(totalTicks / completedAttempts.Count);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
