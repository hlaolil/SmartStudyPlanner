using SmartStudyPlanner.Models;
using SmartStudyPlanner.Services;

namespace SmartStudyPlanner.Tests;

public class SpacedRepetitionTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void Good_answers_grow_the_interval()
    {
        var c = new Flashcard { DueDate = Today };
        var intervals = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            SpacedRepetition.Apply(c, ReviewGrade.Good, c.DueDate);
            intervals.Add(c.IntervalDays);
        }
        Assert.Equal(new[] { 1, 4, 10, 25, 63 }, intervals);
    }

    [Fact]
    public void Again_resets_and_lowers_ease()
    {
        var c = new Flashcard { DueDate = Today, Repetitions = 4, IntervalDays = 30, Ease = 2.5 };
        SpacedRepetition.Apply(c, ReviewGrade.Again, Today);
        Assert.Equal(0, c.Repetitions);
        Assert.Equal(Today, c.DueDate);
        Assert.Equal(2.3, c.Ease, 3);
        Assert.Equal(1, c.Lapses);
    }

    [Fact]
    public void Ease_never_drops_below_minimum()
    {
        var c = new Flashcard { DueDate = Today };
        for (var i = 0; i < 20; i++) SpacedRepetition.Apply(c, ReviewGrade.Again, Today);
        Assert.Equal(SpacedRepetition.MinEase, c.Ease, 3);
    }

    [Fact]
    public void Interval_is_capped_so_card_is_seen_before_exam()
    {
        var c = new Flashcard { DueDate = Today, Repetitions = 3, IntervalDays = 20, Ease = 2.5 };
        SpacedRepetition.Apply(c, ReviewGrade.Easy, Today, nextExam: Today.AddDays(10));
        Assert.Equal(5, c.IntervalDays);
        Assert.True(c.DueDate < Today.AddDays(10));
    }

    [Fact]
    public void Preview_does_not_change_the_card()
    {
        var c = new Flashcard { DueDate = Today, Repetitions = 2, IntervalDays = 4, Ease = 2.5 };
        var preview = SpacedRepetition.PreviewInterval(c, ReviewGrade.Good, Today);
        Assert.Equal(10, preview);
        Assert.Equal(4, c.IntervalDays);
    }
}

public class AnalyticsTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void Streak_counts_consecutive_days_and_tolerates_not_yet_studied_today()
    {
        var d = new AppData();
        foreach (var ago in new[] { 1, 2, 3, 5, 6, 7, 8 })
            d.Logs.Add(new StudyLog { Date = Today.AddDays(-ago), Minutes = 30 });

        var s = Analytics.Streak(d, Today);

        Assert.Equal(3, s.Current);
        Assert.Equal(4, s.Best);
        Assert.False(s.StudiedToday);
    }

    [Fact]
    public void Grade_summary_computes_current_and_needed()
    {
        var subject = new Subject { Name = "Math", TargetGrade = 80 };
        var d = new AppData { Subjects = { subject } };
        d.Tasks.Add(new StudyTask { SubjectId = subject.Id, WeightPercent = 30, ScorePercent = 70 });
        d.Tasks.Add(new StudyTask { SubjectId = subject.Id, WeightPercent = 20, ScorePercent = 90 });
        d.Tasks.Add(new StudyTask { SubjectId = subject.Id, WeightPercent = 50 });

        var g = Analytics.Grades(d, subject);

        Assert.Equal(78, g.CurrentPercent!.Value, 3);   // (21 + 18) / 50
        Assert.Equal(50, g.RemainingWeight, 3);
        Assert.Equal(82, g.NeededOnRemaining!.Value, 3); // (80 - 39) / 50
    }

    [Fact]
    public void Calibration_needs_three_samples_and_is_clamped()
    {
        var d = new AppData();
        void Add(double est, int actual)
        {
            var t = new StudyTask { EstimatedHours = est, Completed = true };
            d.Tasks.Add(t);
            d.Logs.Add(new StudyLog { TaskId = t.Id, Minutes = actual, Date = Today });
        }
        Add(1, 300);
        Add(1, 300);
        Assert.Equal(1, Analytics.CalibrationFactor(d));
        Add(1, 300);
        Assert.Equal(1.75, Analytics.CalibrationFactor(d));
    }

    [Fact]
    public void Backup_round_trips_through_json()
    {
        var data = SampleData.Create(Today);
        var json = System.Text.Json.JsonSerializer.Serialize(data, PlannerStore.JsonOptions);
        var back = PlannerStore.Deserialize(json)!;
        Assert.Equal(data.Tasks.Count, back.Tasks.Count);
        Assert.Equal(data.Tasks[0].DueDate, back.Tasks[0].DueDate);
        Assert.Equal(data.Cards.Count, back.Cards.Count);
    }

    [Fact]
    public void Ics_export_contains_events()
    {
        var data = SampleData.Create(Today);
        var plan = Scheduler.Build(data, Today.ToDateTime(new TimeOnly(9, 0)));
        var ics = IcsExporter.Build(plan, data);
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }
}
