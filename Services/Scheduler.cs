using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

public enum BlockKind { Task, Review }

public record PlannedBlock(
    DateOnly Date,
    TimeOnly Start,
    int Minutes,
    BlockKind Kind,
    Guid? TaskId,
    Guid? SubjectId,
    string Title)
{
    public TimeOnly End => Start.AddMinutes(Minutes);
}

public enum ForecastStatus { OnTrack, Tight, AtRisk, Overdue, ReadyToComplete }

public record TaskForecast(
    Guid TaskId,
    int RemainingMinutes,
    int ScheduledMinutes,
    DateOnly? LastSessionDate,
    ForecastStatus Status)
{
    public int ShortfallMinutes => Math.Max(0, RemainingMinutes - ScheduledMinutes);
}

public class SchedulePlan
{
    public DateOnly Today { get; init; }
    public List<PlannedBlock> Blocks { get; } = new();
    public Dictionary<Guid, TaskForecast> Forecasts { get; } = new();
    /// <summary>Study minutes still available on each day of the horizon.</summary>
    public Dictionary<DateOnly, int> Capacity { get; } = new();
    public double CalibrationFactor { get; init; } = 1;

    public IEnumerable<PlannedBlock> ForDay(DateOnly date) =>
        Blocks.Where(b => b.Date == date).OrderBy(b => b.Start);

    public int PlannedMinutes(DateOnly date) => Blocks.Where(b => b.Date == date).Sum(b => b.Minutes);

    public TaskForecast? ForecastFor(Guid taskId) => Forecasts.GetValueOrDefault(taskId);

    public IEnumerable<TaskForecast> AtRisk =>
        Forecasts.Values.Where(f => f.Status is ForecastStatus.AtRisk or ForecastStatus.Overdue);
}

/// <summary>
/// Builds a study plan by distributing each task's remaining work across the
/// student's available time before its deadline.
///
/// The algorithm walks forward day by day. On each day it repeatedly picks the
/// task with the highest "pressure": the share of the remaining capacity before
/// its (buffered) deadline that the task still needs, weighted by priority and
/// grade weight. Work is interleaved (a cap on blocks per task per day), and
/// exams are deliberately spread out (distributed practice) until they become
/// urgent. Due flashcards get a short review block first.
/// </summary>
public static class Scheduler
{
    public const int MaxHorizonDays = 180;
    public const int MinHorizonDays = 28;

    public static SchedulePlan Build(AppData data, DateTime localNow)
    {
        var s = data.Settings;
        var today = DateOnly.FromDateTime(localNow);
        var calibration = s.UseCalibration ? Analytics.CalibrationFactor(data) : 1.0;
        var plan = new SchedulePlan { Today = today, CalibrationFactor = calibration };

        var loggedByTask = data.Logs
            .Where(l => l.TaskId.HasValue)
            .GroupBy(l => l.TaskId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Minutes));

        // Remaining work per open task.
        var work = new List<WorkItem>();
        foreach (var t in data.Tasks.Where(t => !t.Completed))
        {
            var logged = loggedByTask.GetValueOrDefault(t.Id);
            var total = (int)Math.Round(Math.Max(0, t.EstimatedHours) * 60 * calibration);
            // Partial credit for checked-off subtasks when nothing (or little) was logged.
            if (t.Subtasks.Count > 0)
            {
                var doneShare = (double)t.Subtasks.Count(x => x.Done) / t.Subtasks.Count;
                logged = Math.Max(logged, (int)(total * doneShare));
            }
            var remaining = Math.Max(0, total - logged);
            work.Add(new WorkItem(t, remaining));
        }

        var latestDue = work.Count == 0 ? today : work.Max(w => w.Task.DueDate);
        var horizon = Math.Clamp(latestDue.DayNumber - today.DayNumber + 1, MinHorizonDays, MaxHorizonDays);

        // Capacity per day.
        var capacity = new int[horizon];
        var loggedToday = data.Logs.Where(l => l.Date == today).Sum(l => l.Minutes);
        var minutesLeftToday = (int)(new TimeOnly(23, 59) - TimeOnly.FromDateTime(localNow)).TotalMinutes;
        var studyShare = s.SessionMinutes / (double)Math.Max(1, s.SessionMinutes + s.BreakMinutes);
        var studyMinutesLeftToday = (int)(Math.Max(0, minutesLeftToday) * studyShare);
        for (var i = 0; i < horizon; i++)
        {
            var d = today.AddDays(i);
            var cap = s.MinutesFor(d);
            if (i == 0)
                cap = Math.Min(Math.Max(0, cap - loggedToday), studyMinutesLeftToday);
            capacity[i] = cap;
        }

        // Reserve review time for flashcards due on each day.
        var review = new int[horizon];
        if (s.ReviewMinutesCap > 0)
        {
            for (var i = 0; i < horizon; i++)
            {
                var d = today.AddDays(i);
                var due = i == 0 ? data.Cards.Count(c => c.DueDate <= d) : data.Cards.Count(c => c.DueDate == d);
                if (due == 0) continue;
                var minutes = RoundUp5((int)Math.Ceiling(due * 1.0));
                review[i] = Math.Min(Math.Min(minutes, s.ReviewMinutesCap), capacity[i]);
            }
        }

        // Suffix sums of capacity available for task work (for pressure scoring).
        var free = new int[horizon];
        for (var i = 0; i < horizon; i++) free[i] = capacity[i] - review[i];
        var prefix = new long[horizon + 1];
        for (var i = 0; i < horizon; i++) prefix[i + 1] = prefix[i] + free[i];
        long FreeBetween(int fromIdx, int toIdx) =>
            toIdx < fromIdx ? 0 : prefix[Math.Min(toIdx, horizon - 1) + 1] - prefix[Math.Max(fromIdx, 0)];

        var minBlock = Math.Max(5, s.MinBlockMinutes);
        var session = Math.Max(minBlock, s.SessionMinutes);
        var maxPerDay = Math.Max(1, s.MaxBlocksPerTaskPerDay);
        var buffer = Math.Max(0, s.BufferDays);

        var scheduled = work.ToDictionary(w => w.Task.Id, _ => 0);
        var lastDate = new Dictionary<Guid, DateOnly>();

        int TargetIdx(StudyTask t, int fromIdx)
        {
            var dueIdx = t.DueDate.DayNumber - today.DayNumber;
            return dueIdx < fromIdx ? fromIdx : Math.Max(fromIdx, dueIdx - buffer);
        }

        // A task can be worked on until its due date; tasks already overdue today stay schedulable (ASAP).
        bool Schedulable(StudyTask t, int idx)
        {
            var dueIdx = t.DueDate.DayNumber - today.DayNumber;
            return dueIdx < 0 || idx <= dueIdx;
        }

        int StartIdx(StudyTask t) => t.StartDate is { } sd ? sd.DayNumber - today.DayNumber : 0;

        Dictionary<WorkItem, int> MustDoToday(int i)
        {
            var active = work.Where(w => w.Remaining > 0 && StartIdx(w.Task) <= i && Schedulable(w.Task, i)).ToList();
            if (active.Count == 0) return new();
            var target = active.ToDictionary(w => w, w => TargetIdx(w.Task, i));
            var left = active.ToDictionary(w => w, w => w.Remaining);
            var byLatest = active.OrderByDescending(w => target[w]).ThenByDescending(w => w.Task.DueDate).ToList();
            var maxTarget = target.Values.Max();
            for (var j = Math.Min(maxTarget, horizon - 1); j > i; j--)
            {
                var cap = free[j];
                foreach (var w in byLatest)
                {
                    if (cap <= 0) break;
                    if (target[w] < j || left[w] == 0 || StartIdx(w.Task) > j) continue;
                    var take = Math.Min(cap, left[w]);
                    left[w] -= take;
                    cap -= take;
                }
            }
            return left.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        for (var i = 0; i < horizon; i++)
        {
            var day = today.AddDays(i);
            var dayBlocks = new List<(BlockKind kind, StudyTask? task, int minutes)>();
            if (review[i] > 0) dayBlocks.Add((BlockKind.Review, null, review[i]));
            var avail = free[i];
            var blocksToday = new Dictionary<Guid, int>();

            // Phase 1: work that cannot be deferred without missing a (buffered) deadline,
            // found by packing everything as late as possible into future days (reverse EDF).
            foreach (var (w, mustMinutes) in MustDoToday(i).OrderBy(x => x.Key.Task.DueDate))
            {
                var need = Math.Min(w.Remaining, Math.Max(mustMinutes, minBlock));
                while (need > 0 && avail >= 5)
                {
                    var len = Math.Min(session, Math.Min(need, avail));
                    dayBlocks.Add((BlockKind.Task, w.Task, len));
                    w.Remaining -= len;
                    need -= len;
                    avail -= len;
                    scheduled[w.Task.Id] += len;
                    lastDate[w.Task.Id] = day;
                    blocksToday[w.Task.Id] = blocksToday.GetValueOrDefault(w.Task.Id) + 1;
                }
            }

            // Phase 2: spread the remaining time by pressure, priority and grade weight.
            while (avail >= 5)
            {
                WorkItem? best = null;
                double bestScore = double.MinValue;
                foreach (var w in work)
                {
                    if (w.Remaining <= 0) continue;
                    var t = w.Task;
                    if (t.StartDate is { } start && start > day) continue;
                    var dueIdx = t.DueDate.DayNumber - today.DayNumber;
                    var overdue = dueIdx < 0;
                    if (!overdue && i > dueIdx) continue;

                    // Soft target leaves a buffer before the deadline when possible.
                    var targetIdx = overdue ? i : Math.Max(i, dueIdx - buffer);
                    var freeUntil = Math.Max(1, FreeBetween(i, targetIdx));
                    var pressure = overdue ? 10.0 : w.Remaining / (double)freeUntil;

                    var count = blocksToday.GetValueOrDefault(t.Id);
                    var limit = maxPerDay;
                    if (t.Kind == TaskKind.Exam && pressure < 0.5) limit = 1; // spaced practice
                    if (pressure >= 1) limit = int.MaxValue; // can't afford to interleave
                    if (count >= limit) continue;

                    var score = pressure
                                * (1 + 0.35 * (int)t.Priority)
                                * (1 + Math.Min(t.WeightPercent ?? 0, 60) / 100.0)
                                - count * 0.05 // prefer variety
                                - dueIdx * 1e-4; // tie-break: earlier deadline first
                    if (score > bestScore) { bestScore = score; best = w; }
                }

                if (best is null) break;

                var len = Math.Min(session, Math.Min(best.Remaining, avail));
                if (len < minBlock && best.Remaining > len)
                {
                    // Not enough room for a useful block; only allow finishing touches.
                    break;
                }
                if (len < 5) break;

                dayBlocks.Add((BlockKind.Task, best.Task, len));
                best.Remaining -= len;
                avail -= len;
                scheduled[best.Task.Id] += len;
                lastDate[best.Task.Id] = day;
                blocksToday[best.Task.Id] = blocksToday.GetValueOrDefault(best.Task.Id) + 1;
            }

            plan.Capacity[day] = capacity[i] - dayBlocks.Sum(b => b.minutes);
            LayOut(plan, data, day, i == 0 ? localNow : null, dayBlocks);
        }

        foreach (var w in work)
        {
            var t = w.Task;
            var initial = w.Remaining + scheduled[t.Id];
            var last = lastDate.TryGetValue(t.Id, out var ld) ? ld : (DateOnly?)null;
            ForecastStatus status;
            if (initial == 0) status = ForecastStatus.ReadyToComplete;
            else if (t.DueDate < today) status = ForecastStatus.Overdue;
            else if (w.Remaining > 0) status = ForecastStatus.AtRisk;
            else if (last.HasValue && last.Value > t.DueDate.AddDays(-buffer)) status = ForecastStatus.Tight;
            else status = ForecastStatus.OnTrack;
            plan.Forecasts[t.Id] = new TaskForecast(t.Id, initial, scheduled[t.Id], last, status);
        }

        return plan;
    }

    private static void LayOut(SchedulePlan plan, AppData data, DateOnly day, DateTime? now,
        List<(BlockKind kind, StudyTask? task, int minutes)> blocks)
    {
        var s = data.Settings;
        var start = s.DayStartTime;
        if (now is { } n)
        {
            var nowT = TimeOnly.FromDateTime(n);
            if (nowT > start)
            {
                var mins = (int)Math.Ceiling(nowT.ToTimeSpan().TotalMinutes / 5.0) * 5;
                start = mins >= 24 * 60 ? new TimeOnly(23, 55) : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(mins));
            }
        }

        var cursor = start;
        var guard = start;
        foreach (var (kind, task, minutes) in blocks)
        {
            var title = kind == BlockKind.Review ? "Flashcard review" : task!.Title;
            plan.Blocks.Add(new PlannedBlock(day, cursor, minutes, kind, task?.Id, task?.SubjectId, title));
            var next = cursor.AddMinutes(minutes + (kind == BlockKind.Review ? 5 : s.BreakMinutes));
            if (next < guard) next = new TimeOnly(23, 59); // never wrap past midnight
            cursor = next;
            guard = next;
        }
    }

    private static int RoundUp5(int minutes) => (int)Math.Ceiling(minutes / 5.0) * 5;

    private sealed class WorkItem(StudyTask task, int remaining)
    {
        public StudyTask Task { get; } = task;
        public int Remaining { get; set; } = remaining;
    }
}
