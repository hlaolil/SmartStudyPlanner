namespace SmartStudyPlanner.Models;

public enum TaskKind { Assignment, Exam, Project, Reading, Other }

public enum Priority { Low = 0, Medium = 1, High = 2 }

public enum ReviewGrade { Again = 0, Hard = 1, Good = 2, Easy = 3 }

public class Subject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#6366f1";
    /// <summary>How many hours per week the student wants to spend on this subject.</summary>
    public double TargetWeeklyHours { get; set; } = 4;
    /// <summary>Final grade (percent) the student is aiming for.</summary>
    public double? TargetGrade { get; set; }
    public bool Archived { get; set; }
}

public class SubTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

public class StudyTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public Guid? SubjectId { get; set; }
    public TaskKind Kind { get; set; } = TaskKind.Assignment;
    public Priority Priority { get; set; } = Priority.Medium;
    public DateOnly DueDate { get; set; }
    /// <summary>Optional: do not schedule work before this date.</summary>
    public DateOnly? StartDate { get; set; }
    public double EstimatedHours { get; set; } = 2;
    public bool Completed { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public string Notes { get; set; } = "";
    public List<SubTask> Subtasks { get; set; } = new();
    /// <summary>Share of the final course grade, in percent (e.g. 20 for 20%).</summary>
    public double? WeightPercent { get; set; }
    /// <summary>Score received, in percent.</summary>
    public double? ScorePercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StudyTask Clone()
    {
        var copy = (StudyTask)MemberwiseClone();
        copy.Subtasks = Subtasks.Select(s => new SubTask { Id = s.Id, Title = s.Title, Done = s.Done }).ToList();
        return copy;
    }
}

public class StudyLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TaskId { get; set; }
    public Guid? SubjectId { get; set; }
    public DateOnly Date { get; set; }
    public int Minutes { get; set; }
    /// <summary>"focus", "plan", "manual" or "review".</summary>
    public string Source { get; set; } = "manual";
    public string Note { get; set; } = "";
    public DateTime LoggedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Flashcard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SubjectId { get; set; }
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public double Ease { get; set; } = 2.5;
    public int IntervalDays { get; set; }
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? LastReviewed { get; set; }
}

public class ActiveTimer
{
    public DateTime StartedUtc { get; set; }
    public int DurationMinutes { get; set; }
    public Guid? TaskId { get; set; }
    public bool IsBreak { get; set; }
    public bool Paused { get; set; }
    /// <summary>Seconds accumulated before the current run segment (for pause/resume).</summary>
    public double ElapsedBeforeSeconds { get; set; }
}

public class Settings
{
    public string StudentName { get; set; } = "";
    /// <summary>Study minutes available per weekday, indexed by <see cref="DayOfWeek"/> (Sunday = 0).</summary>
    public int[] AvailabilityMinutes { get; set; } = { 120, 150, 150, 150, 150, 90, 180 };
    /// <summary>Time the study window starts each day, "HH:mm".</summary>
    public string DayStart { get; set; } = "17:00";
    public int SessionMinutes { get; set; } = 50;
    public int BreakMinutes { get; set; } = 10;
    public int MinBlockMinutes { get; set; } = 20;
    public int MaxBlocksPerTaskPerDay { get; set; } = 2;
    /// <summary>Aim to finish work this many days before the deadline.</summary>
    public int BufferDays { get; set; } = 1;
    public int DailyGoalMinutes { get; set; } = 120;
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int ReviewMinutesCap { get; set; } = 20;
    /// <summary>Scale remaining work by the student's historical estimate accuracy.</summary>
    public bool UseCalibration { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool Notifications { get; set; }
    /// <summary>Per-date overrides of available minutes, keyed "yyyy-MM-dd".</summary>
    public Dictionary<string, int> DayOverrides { get; set; } = new();

    public TimeOnly DayStartTime =>
        TimeOnly.TryParse(DayStart, out var t) ? t : new TimeOnly(17, 0);

    public int BaseMinutesFor(DayOfWeek day) =>
        AvailabilityMinutes is { Length: 7 } ? Math.Max(0, AvailabilityMinutes[(int)day]) : 120;

    public int MinutesFor(DateOnly date) =>
        DayOverrides.TryGetValue(date.ToString("yyyy-MM-dd"), out var m) ? Math.Max(0, m) : BaseMinutesFor(date.DayOfWeek);
}

public class AppData
{
    public int Version { get; set; } = 1;
    public List<Subject> Subjects { get; set; } = new();
    public List<StudyTask> Tasks { get; set; } = new();
    public List<StudyLog> Logs { get; set; } = new();
    public List<Flashcard> Cards { get; set; } = new();
    public Settings Settings { get; set; } = new();
    public ActiveTimer? Timer { get; set; }
    public bool Onboarded { get; set; }
}
