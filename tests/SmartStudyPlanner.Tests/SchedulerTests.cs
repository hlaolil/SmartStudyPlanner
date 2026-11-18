using SmartStudyPlanner.Models;
using SmartStudyPlanner.Services;

namespace SmartStudyPlanner.Tests;

public class SchedulerTests
{
    // A Monday morning, before the default 17:00 study start.
    private static readonly DateTime Now = new(2026, 11, 16, 9, 0, 0);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static AppData Data(int minutesPerDay = 120, Action<Settings>? configure = null)
    {
        var d = new AppData { Onboarded = true };
        d.Settings.AvailabilityMinutes = Enumerable.Repeat(minutesPerDay, 7).ToArray();
        d.Settings.UseCalibration = false;
        configure?.Invoke(d.Settings);
        return d;
    }

    private static StudyTask Task(string title, int dueIn, double hours, Priority p = Priority.Medium, TaskKind kind = TaskKind.Assignment) =>
        new() { Title = title, DueDate = Today.AddDays(dueIn), EstimatedHours = hours, Priority = p, Kind = kind };

    [Fact]
    public void Schedules_all_work_before_deadline_when_it_fits()
    {
        var data = Data();
        var t = Task("Essay", 5, 4);
        data.Tasks.Add(t);

        var plan = Scheduler.Build(data, Now);

        Assert.Equal(240, plan.Blocks.Where(b => b.TaskId == t.Id).Sum(b => b.Minutes));
        Assert.All(plan.Blocks, b => Assert.True(b.Date < t.DueDate));
        Assert.Equal(ForecastStatus.OnTrack, plan.ForecastFor(t.Id)!.Status);
    }

    [Fact]
    public void Never_exceeds_daily_capacity()
    {
        var data = Data(90);
        for (var i = 0; i < 6; i++) data.Tasks.Add(Task($"T{i}", 3 + i, 3));

        var plan = Scheduler.Build(data, Now);

        foreach (var day in plan.Blocks.GroupBy(b => b.Date))
            Assert.True(day.Sum(b => b.Minutes) <= 90, $"{day.Key} over capacity");
    }

    [Fact]
    public void Urgent_low_priority_work_is_not_starved_by_distant_high_priority_work()
    {
        // The bug this guards against: a heavy, high-priority, far-off project crowding out
        // a small task due in two days until it no longer fits.
        var data = Data(120);
        var soon = Task("Reading", 2, 3, Priority.Low);
        var big = Task("Project", 20, 20, Priority.High);
        big.WeightPercent = 40;
        data.Tasks.AddRange(new[] { soon, big });

        var plan = Scheduler.Build(data, Now);

        Assert.Equal(180, plan.ForecastFor(soon.Id)!.ScheduledMinutes);
        Assert.NotEqual(ForecastStatus.AtRisk, plan.ForecastFor(soon.Id)!.Status);
        Assert.NotEqual(ForecastStatus.AtRisk, plan.ForecastFor(big.Id)!.Status);
    }

    [Fact]
    public void Finds_a_feasible_plan_whenever_one_exists()
    {
        // Total demand equals total capacity before the last deadline: only an EDF-style plan fits.
        var data = Data(60, s => s.BufferDays = 0);
        var a = Task("A", 1, 1);           // must use all of day 0
        var b = Task("B", 3, 2, Priority.High); // days 1–2
        var c = Task("C", 4, 1, Priority.Low);  // day 3
        data.Tasks.AddRange(new[] { a, b, c });

        var plan = Scheduler.Build(data, Now);

        Assert.All(new[] { a, b, c }, t => Assert.Equal(0, plan.ForecastFor(t.Id)!.ShortfallMinutes));
    }

    [Fact]
    public void Reports_shortfall_when_work_cannot_fit()
    {
        var data = Data(60);
        var t = Task("Cram", 2, 10);
        data.Tasks.Add(t);

        var f = Scheduler.Build(data, Now).ForecastFor(t.Id)!;

        Assert.Equal(ForecastStatus.AtRisk, f.Status);
        Assert.Equal(600 - 180, f.ShortfallMinutes); // today, tomorrow and the due day itself
    }

    [Fact]
    public void Logged_time_reduces_remaining_work_and_todays_capacity()
    {
        var data = Data(120);
        var t = Task("Lab", 4, 3);
        data.Tasks.Add(t);
        data.Logs.Add(new StudyLog { TaskId = t.Id, Date = Today, Minutes = 60 });

        var plan = Scheduler.Build(data, Now);

        Assert.Equal(120, plan.ForecastFor(t.Id)!.RemainingMinutes);
        Assert.True(plan.PlannedMinutes(Today) <= 60);
    }

    [Fact]
    public void Respects_start_date_and_day_overrides()
    {
        var data = Data(120);
        data.Settings.DayOverrides[Today.AddDays(1).ToString("yyyy-MM-dd")] = 0;
        var t = Task("Later", 6, 2);
        t.StartDate = Today.AddDays(1);
        data.Tasks.Add(t);

        var plan = Scheduler.Build(data, Now);

        Assert.DoesNotContain(plan.Blocks, b => b.Date <= Today.AddDays(1));
    }

    [Fact]
    public void Completed_tasks_are_not_scheduled_and_overdue_tasks_are_flagged()
    {
        var data = Data();
        var done = Task("Done", 3, 2);
        done.Completed = true;
        var late = Task("Late", -2, 1);
        data.Tasks.AddRange(new[] { done, late });

        var plan = Scheduler.Build(data, Now);

        Assert.DoesNotContain(plan.Blocks, b => b.TaskId == done.Id);
        Assert.Equal(ForecastStatus.Overdue, plan.ForecastFor(late.Id)!.Status);
        Assert.Contains(plan.Blocks, b => b.TaskId == late.Id && b.Date == Today);
    }

    [Fact]
    public void Exams_are_spread_out_rather_than_crammed()
    {
        var data = Data(240, s => s.MaxBlocksPerTaskPerDay = 3);
        var exam = Task("Midterm", 10, 5, kind: TaskKind.Exam);
        data.Tasks.Add(exam);

        var plan = Scheduler.Build(data, Now);
        var studyDays = plan.Blocks.Where(b => b.TaskId == exam.Id).Select(b => b.Date).Distinct().Count();

        Assert.True(studyDays >= 5, $"exam studied on only {studyDays} days");
    }

    [Fact]
    public void Due_flashcards_get_a_review_block_first()
    {
        var data = Data();
        data.Cards.AddRange(Enumerable.Range(0, 12).Select(_ => new Flashcard { Front = "q", Back = "a", DueDate = Today }));
        data.Tasks.Add(Task("Essay", 4, 2));

        var first = Scheduler.Build(data, Now).ForDay(Today).First();

        Assert.Equal(BlockKind.Review, first.Kind);
        Assert.InRange(first.Minutes, 10, data.Settings.ReviewMinutesCap);
    }

    [Fact]
    public void Blocks_are_laid_out_sequentially_from_day_start_with_breaks()
    {
        var data = Data(150, s => { s.DayStart = "18:00"; s.SessionMinutes = 50; s.BreakMinutes = 10; });
        data.Tasks.Add(Task("Big", 3, 6));

        var blocks = Scheduler.Build(data, Now).ForDay(Today).ToList();

        Assert.Equal(new TimeOnly(18, 0), blocks[0].Start);
        for (var i = 1; i < blocks.Count; i++)
            Assert.Equal(blocks[i - 1].End.AddMinutes(10), blocks[i].Start);
    }

    [Fact]
    public void Later_in_the_day_only_remaining_time_is_planned()
    {
        var data = Data(300);
        data.Tasks.Add(Task("Big", 5, 10));
        var lateEvening = new DateTime(2026, 11, 16, 22, 0, 0);

        var plan = Scheduler.Build(data, lateEvening);

        Assert.True(plan.PlannedMinutes(Today) <= 120);
        Assert.All(plan.ForDay(Today), b => Assert.True(b.Start >= new TimeOnly(22, 0)));
    }

    [Fact]
    public void Calibration_scales_remaining_work()
    {
        var data = Data(240);
        data.Settings.UseCalibration = true;
        for (var i = 0; i < 3; i++)
        {
            var past = Task($"Past{i}", -5, 1);
            past.Completed = true;
            data.Tasks.Add(past);
            data.Logs.Add(new StudyLog { TaskId = past.Id, Date = Today.AddDays(-6), Minutes = 90 });
        }
        var t = Task("New", 7, 2);
        data.Tasks.Add(t);

        var plan = Scheduler.Build(data, Now);

        Assert.Equal(1.5, plan.CalibrationFactor, 3);
        Assert.Equal(180, plan.ForecastFor(t.Id)!.RemainingMinutes);
    }

    [Fact]
    public void Sample_semester_builds_a_sane_plan()
    {
        var data = SampleData.Create(Today);
        var plan = Scheduler.Build(data, Now);

        Assert.NotEmpty(plan.Blocks);
        foreach (var day in plan.Blocks.GroupBy(b => b.Date))
            Assert.True(day.Sum(b => b.Minutes) <= data.Settings.MinutesFor(day.Key));
        // Short, near-term tasks must fit.
        var reading = data.Tasks.Single(t => t.Title.StartsWith("Read chapters"));
        Assert.Equal(0, plan.ForecastFor(reading.Id)!.ShortfallMinutes);
    }
}
