using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

/// <summary>
/// Per-circuit application state. Data lives in the browser's localStorage, so the
/// app needs no database or account and works offline-first; export/import moves
/// it between devices.
/// </summary>
public record TimerResult(bool WasBreak, int MinutesLogged, Guid? TaskId);

public sealed class PlannerStore(IJSRuntime js, ILogger<PlannerStore> logger)
{
    public const string StorageKey = "ssp.data.v1";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    private SchedulePlan? _plan;

    public AppData Data { get; private set; } = new();
    public bool Loaded { get; private set; }
    public int TimezoneOffsetMinutes { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Outcome of the most recent timer that ran to completion (not persisted).</summary>
    public TimerResult? LastTimerResult { get; set; }

    /// <summary>Raised after any change to the data.</summary>
    public event Action? Changed;

    public DateTime Now => DateTime.UtcNow.AddMinutes(-TimezoneOffsetMinutes);
    public DateOnly Today => DateOnly.FromDateTime(Now);

    public SchedulePlan Plan
    {
        get
        {
            if (_plan is null || _plan.Today != Today) _plan = Scheduler.Build(Data, Now);
            return _plan;
        }
    }

    public async Task LoadAsync()
    {
        if (Loaded) return;
        try
        {
            TimezoneOffsetMinutes = await js.InvokeAsync<int>("ssp.tzOffset");
            var json = await js.InvokeAsync<string?>("ssp.load", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
                Data = Deserialize(json) ?? new AppData();
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Could not load saved data");
            LastError = "Your saved data could not be read, so the app started empty.";
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Saved data was corrupt");
            LastError = "Your saved data was corrupt, so the app started empty.";
        }
        Normalize(Data);
        Loaded = true;
    }

    public static AppData? Deserialize(string json) => JsonSerializer.Deserialize<AppData>(json, JsonOptions);

    public string Export() => JsonSerializer.Serialize(Data, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });

    private static void Normalize(AppData data)
    {
        data.Settings ??= new Settings();
        if (data.Settings.AvailabilityMinutes is not { Length: 7 })
            data.Settings.AvailabilityMinutes = new Settings().AvailabilityMinutes;
        data.Settings.DayOverrides ??= new();
        data.Subjects ??= new();
        data.Tasks ??= new();
        data.Logs ??= new();
        data.Cards ??= new();
        foreach (var t in data.Tasks) t.Subtasks ??= new();
    }

    public async Task SaveAsync()
    {
        _plan = null;
        try
        {
            await js.InvokeVoidAsync("ssp.save", StorageKey, JsonSerializer.Serialize(Data, JsonOptions));
            LastError = null;
        }
        catch (JSDisconnectedException)
        {
            // Circuit is gone; nothing to do.
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Could not save data");
            LastError = "Saving failed — your browser storage may be full or disabled.";
        }
        Changed?.Invoke();
    }

    public async Task MutateAsync(Action<AppData> change)
    {
        change(Data);
        await SaveAsync();
    }

    // ---------- Lookups ----------

    public Subject? SubjectById(Guid? id) => id is { } g ? Data.Subjects.FirstOrDefault(s => s.Id == g) : null;
    public StudyTask? TaskById(Guid? id) => id is { } g ? Data.Tasks.FirstOrDefault(t => t.Id == g) : null;
    public string SubjectColor(Guid? id) => SubjectById(id)?.Color ?? "#94a3b8";
    public string SubjectName(Guid? id) => SubjectById(id)?.Name ?? "General";

    public IEnumerable<Subject> ActiveSubjects => Data.Subjects.Where(s => !s.Archived).OrderBy(s => s.Name);

    public IEnumerable<StudyTask> OpenTasks => Data.Tasks.Where(t => !t.Completed).OrderBy(t => t.DueDate);

    public DateOnly? NextExamFor(Guid? subjectId) =>
        subjectId is null
            ? null
            : Data.Tasks.Where(t => t.SubjectId == subjectId && t.Kind == TaskKind.Exam && !t.Completed && t.DueDate > Today)
                .OrderBy(t => t.DueDate).Select(t => (DateOnly?)t.DueDate).FirstOrDefault();

    public int DueCardCount => Data.Cards.Count(c => c.DueDate <= Today);

    // ---------- Tasks ----------

    public Task UpsertTaskAsync(StudyTask edited) => MutateAsync(d =>
    {
        var idx = d.Tasks.FindIndex(t => t.Id == edited.Id);
        if (idx >= 0) d.Tasks[idx] = edited;
        else d.Tasks.Add(edited);
    });

    public Task DeleteTaskAsync(Guid id) => MutateAsync(d =>
    {
        d.Tasks.RemoveAll(t => t.Id == id);
        foreach (var l in d.Logs.Where(l => l.TaskId == id)) l.TaskId = null; // keep study history
        if (d.Timer?.TaskId == id) d.Timer.TaskId = null;
    });

    public Task SetCompletedAsync(Guid id, bool completed) => MutateAsync(d =>
    {
        var t = d.Tasks.FirstOrDefault(x => x.Id == id);
        if (t is null) return;
        t.Completed = completed;
        t.CompletedOn = completed ? Today : null;
    });

    public Task LogAsync(Guid? taskId, int minutes, string source, DateOnly? date = null, Guid? subjectId = null) => MutateAsync(d =>
    {
        if (minutes <= 0) return;
        var task = d.Tasks.FirstOrDefault(t => t.Id == taskId);
        d.Logs.Add(new StudyLog
        {
            TaskId = task?.Id,
            SubjectId = task?.SubjectId ?? subjectId,
            Date = date ?? Today,
            Minutes = minutes,
            Source = source
        });
    });

    public Task DeleteLogAsync(Guid id) => MutateAsync(d => d.Logs.RemoveAll(l => l.Id == id));

    // ---------- Subjects ----------

    public Task UpsertSubjectAsync(Subject edited) => MutateAsync(d =>
    {
        var idx = d.Subjects.FindIndex(s => s.Id == edited.Id);
        if (idx >= 0) d.Subjects[idx] = edited;
        else d.Subjects.Add(edited);
    });

    public Task DeleteSubjectAsync(Guid id) => MutateAsync(d =>
    {
        d.Subjects.RemoveAll(s => s.Id == id);
        foreach (var t in d.Tasks.Where(t => t.SubjectId == id)) t.SubjectId = null;
        foreach (var c in d.Cards.Where(c => c.SubjectId == id)) c.SubjectId = null;
    });

    // ---------- Flashcards ----------

    public Task AddCardsAsync(IEnumerable<Flashcard> cards) => MutateAsync(d =>
    {
        foreach (var c in cards)
        {
            if (c.DueDate == default) c.DueDate = Today;
            d.Cards.Add(c);
        }
    });

    public Task DeleteCardAsync(Guid id) => MutateAsync(d => d.Cards.RemoveAll(c => c.Id == id));

    public Task ReviewCardAsync(Flashcard card, ReviewGrade grade) => MutateAsync(_ =>
        SpacedRepetition.Apply(card, grade, Today, NextExamFor(card.SubjectId)));

    // ---------- Availability ----------

    public Task SetDayOverrideAsync(DateOnly date, int? minutes) => MutateAsync(d =>
    {
        var key = date.ToString("yyyy-MM-dd");
        if (minutes is null) d.Settings.DayOverrides.Remove(key);
        else d.Settings.DayOverrides[key] = Math.Max(0, minutes.Value);
        // Drop stale overrides.
        foreach (var k in d.Settings.DayOverrides.Keys.ToList())
            if (DateOnly.TryParse(k, out var kd) && kd < Today.AddDays(-30)) d.Settings.DayOverrides.Remove(k);
    });

    // ---------- Whole data set ----------

    public async Task ReplaceAllAsync(AppData data)
    {
        Normalize(data);
        Data = data;
        await SaveAsync();
    }

    public Task LoadSampleAsync() => ReplaceAllAsync(SampleData.Create(Today, Data.Settings));

    public Task ResetAsync() => ReplaceAllAsync(new AppData { Onboarded = true });

    // ---------- Focus timer ----------

    public double TimerElapsedSeconds(ActiveTimer t) =>
        t.ElapsedBeforeSeconds + (t.Paused ? 0 : Math.Max(0, (DateTime.UtcNow - t.StartedUtc).TotalSeconds));

    public double TimerRemainingSeconds(ActiveTimer t) =>
        Math.Max(0, t.DurationMinutes * 60 - TimerElapsedSeconds(t));

    public Task StartTimerAsync(Guid? taskId, int minutes, bool isBreak = false) => MutateAsync(d =>
        d.Timer = new ActiveTimer
        {
            StartedUtc = DateTime.UtcNow, DurationMinutes = Math.Clamp(minutes, 1, 240),
            TaskId = taskId, IsBreak = isBreak
        });

    public Task PauseTimerAsync() => MutateAsync(d =>
    {
        if (d.Timer is not { Paused: false } t) return;
        t.ElapsedBeforeSeconds = TimerElapsedSeconds(t);
        t.Paused = true;
    });

    public Task ResumeTimerAsync() => MutateAsync(d =>
    {
        if (d.Timer is not { Paused: true } t) return;
        t.StartedUtc = DateTime.UtcNow;
        t.Paused = false;
    });

    /// <summary>Stops the timer, logging focused minutes. Returns minutes logged.</summary>
    public async Task<int> StopTimerAsync(bool log = true)
    {
        if (Data.Timer is not { } t) return 0;
        var minutes = (int)Math.Round(Math.Min(TimerElapsedSeconds(t), t.DurationMinutes * 60) / 60);
        Data.Timer = null;
        if (log && !t.IsBreak && minutes >= 1)
        {
            await LogAsync(t.TaskId, minutes, "focus");
            return minutes;
        }
        await SaveAsync();
        return 0;
    }
}
