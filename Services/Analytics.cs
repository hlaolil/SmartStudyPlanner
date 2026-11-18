using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

public record StreakInfo(int Current, int Best, bool StudiedToday);

public record GradeSummary(
    double? CurrentPercent,
    double GradedWeight,
    double RemainingWeight,
    double? NeededOnRemaining,
    double? ProjectedFinal);

public static class Analytics
{
    public static Dictionary<DateOnly, int> MinutesByDay(AppData data) =>
        data.Logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.Sum(l => l.Minutes));

    public static int MinutesBetween(AppData data, DateOnly from, DateOnly toInclusive) =>
        data.Logs.Where(l => l.Date >= from && l.Date <= toInclusive).Sum(l => l.Minutes);

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7; // Monday-based weeks
        return date.AddDays(-diff);
    }

    public static StreakInfo Streak(AppData data, DateOnly today)
    {
        var days = data.Logs.Where(l => l.Minutes > 0).Select(l => l.Date).ToHashSet();
        var studiedToday = days.Contains(today);
        var current = 0;
        var cursor = studiedToday ? today : today.AddDays(-1);
        while (days.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }

        var best = 0;
        var run = 0;
        DateOnly? prev = null;
        foreach (var d in days.OrderBy(d => d))
        {
            run = prev.HasValue && d.DayNumber - prev.Value.DayNumber == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }
        return new StreakInfo(current, Math.Max(best, current), studiedToday);
    }

    public static int TaskLoggedMinutes(AppData data, Guid taskId) =>
        data.Logs.Where(l => l.TaskId == taskId).Sum(l => l.Minutes);

    /// <summary>Progress 0..1 for a task based on logged time and subtasks.</summary>
    public static double TaskProgress(AppData data, StudyTask task)
    {
        if (task.Completed) return 1;
        var est = Math.Max(1, task.EstimatedHours * 60);
        var timeShare = Math.Min(1, TaskLoggedMinutes(data, task.Id) / est);
        if (task.Subtasks.Count == 0) return timeShare;
        var subShare = (double)task.Subtasks.Count(s => s.Done) / task.Subtasks.Count;
        return Math.Max(timeShare, subShare);
    }

    /// <summary>
    /// Ratio between actual and estimated time for completed tasks with logged work.
    /// Used to correct future estimates. Needs at least 3 samples; clamped to [0.75, 1.75].
    /// </summary>
    public static double CalibrationFactor(AppData data)
    {
        var samples = EstimateSamples(data);
        if (samples.Count < 3) return 1;
        var est = samples.Sum(s => s.estimated);
        var act = samples.Sum(s => s.actual);
        if (est <= 0) return 1;
        return Math.Clamp(act / est, 0.75, 1.75);
    }

    public static List<(StudyTask task, double estimated, double actual)> EstimateSamples(AppData data)
    {
        var logged = data.Logs.Where(l => l.TaskId.HasValue)
            .GroupBy(l => l.TaskId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Minutes));
        return data.Tasks
            .Where(t => t.Completed && t.EstimatedHours > 0 && logged.GetValueOrDefault(t.Id) >= 15)
            .Select(t => (t, t.EstimatedHours * 60, (double)logged[t.Id]))
            .ToList();
    }

    public static double? OnTimeRate(AppData data)
    {
        var done = data.Tasks.Where(t => t.Completed && t.CompletedOn.HasValue).ToList();
        if (done.Count == 0) return null;
        return done.Count(t => t.CompletedOn!.Value <= t.DueDate) / (double)done.Count;
    }

    public static GradeSummary Grades(AppData data, Subject subject)
    {
        var weighted = data.Tasks.Where(t => t.SubjectId == subject.Id && t.WeightPercent is > 0).ToList();
        var graded = weighted.Where(t => t.ScorePercent.HasValue).ToList();
        var gradedWeight = graded.Sum(t => t.WeightPercent!.Value);
        var earned = graded.Sum(t => t.WeightPercent!.Value * t.ScorePercent!.Value / 100.0);
        var totalWeight = Math.Max(100, weighted.Sum(t => t.WeightPercent!.Value));
        var remainingWeight = Math.Max(0, totalWeight - gradedWeight);

        double? current = gradedWeight > 0 ? earned / gradedWeight * 100 : null;
        double? needed = null;
        if (subject.TargetGrade is { } target && remainingWeight > 0)
            needed = (target / 100.0 * totalWeight - earned) / remainingWeight * 100;
        double? projected = current.HasValue ? (earned + current.Value / 100 * remainingWeight) / totalWeight * 100 : null;
        return new GradeSummary(current, gradedWeight, remainingWeight, needed, projected);
    }

    public static string FormatMinutes(int minutes)
    {
        if (minutes < 60) return $"{minutes}m";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    public static string FormatHours(double hours) =>
        hours < 10 ? hours.ToString("0.#") + "h" : Math.Round(hours).ToString("0") + "h";

    public static string RelativeDue(DateOnly due, DateOnly today)
    {
        var d = due.DayNumber - today.DayNumber;
        return d switch
        {
            < -1 => $"{-d} days overdue",
            -1 => "1 day overdue",
            0 => "Due today",
            1 => "Due tomorrow",
            < 7 => $"Due {due:dddd}",
            _ => $"Due in {d} days"
        };
    }
}
