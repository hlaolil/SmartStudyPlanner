using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

/// <summary>A realistic demo semester so new users can explore every feature immediately.</summary>
public static class SampleData
{
    public static AppData Create(DateOnly today, Settings? keepSettings = null)
    {
        var math = new Subject { Name = "Calculus", Color = "#6366f1", TargetWeeklyHours = 6, TargetGrade = 85 };
        var bio = new Subject { Name = "Biology", Color = "#10b981", TargetWeeklyHours = 5, TargetGrade = 90 };
        var hist = new Subject { Name = "History", Color = "#f59e0b", TargetWeeklyHours = 3, TargetGrade = 80 };
        var cs = new Subject { Name = "Programming", Color = "#ec4899", TargetWeeklyHours = 6, TargetGrade = 90 };

        var data = new AppData
        {
            Onboarded = true,
            Settings = keepSettings ?? new Settings(),
            Subjects = { math, bio, hist, cs }
        };

        StudyTask T(string title, Subject s, TaskKind kind, int dueIn, double hours, Priority p = Priority.Medium,
            double? weight = null, double? score = null, bool done = false, int? doneAgo = null) => new()
        {
            Title = title, SubjectId = s.Id, Kind = kind, DueDate = today.AddDays(dueIn), EstimatedHours = hours,
            Priority = p, WeightPercent = weight, ScorePercent = score, Completed = done,
            CompletedOn = done ? today.AddDays(-(doneAgo ?? 0)) : null
        };

        var pset = T("Problem set 6: integration by parts", math, TaskKind.Assignment, 3, 4, Priority.Medium, 5);
        pset.Subtasks.AddRange(new[]
        {
            new SubTask { Title = "Questions 1–5", Done = true },
            new SubTask { Title = "Questions 6–10" },
            new SubTask { Title = "Check answers & write up" }
        });

        var midterm = T("Calculus midterm", math, TaskKind.Exam, 12, 8, Priority.High, 25);
        var labReport = T("Enzyme kinetics lab report", bio, TaskKind.Assignment, 5, 5, Priority.High, 10);
        var bioQuiz = T("Cell signalling quiz", bio, TaskKind.Exam, 8, 3, Priority.Medium, 5);
        var essay = T("Essay: causes of the French Revolution", hist, TaskKind.Assignment, 9, 7, Priority.Medium, 20);
        essay.Subtasks.AddRange(new[]
        {
            new SubTask { Title = "Research & sources" },
            new SubTask { Title = "Outline" },
            new SubTask { Title = "First draft" },
            new SubTask { Title = "Edit & cite" }
        });
        var reading = T("Read chapters 7–8", hist, TaskKind.Reading, 2, 1.5, Priority.Low);
        var project = T("Final project: budgeting web app", cs, TaskKind.Project, 21, 12, Priority.High, 30);
        var csHw = T("Linked lists exercises", cs, TaskKind.Assignment, 4, 2.5);

        var past = new[]
        {
            T("Problem set 5", math, TaskKind.Assignment, -6, 3, weight: 5, score: 88, done: true, doneAgo: 7),
            T("Quiz 2: limits", math, TaskKind.Exam, -10, 2, weight: 5, score: 76, done: true, doneAgo: 10),
            T("Microscopy lab report", bio, TaskKind.Assignment, -4, 4, weight: 10, score: 92, done: true, doneAgo: 5),
            T("Source analysis", hist, TaskKind.Assignment, -8, 3, weight: 10, score: 81, done: true, doneAgo: 8),
            T("Recursion assignment", cs, TaskKind.Assignment, -3, 3, weight: 10, score: 95, done: true, doneAgo: 3),
        };

        data.Tasks.AddRange(new[] { pset, midterm, labReport, bioQuiz, essay, reading, project, csHw });
        data.Tasks.AddRange(past);

        // Two weeks of study history.
        var rng = new Random(42);
        var history = new (StudyTask task, int ago, int minutes)[]
        {
            (past[0], 9, 90), (past[0], 8, 120), (past[1], 11, 100), (past[2], 6, 150), (past[2], 5, 140),
            (past[3], 9, 110), (past[3], 8, 90), (past[4], 4, 120), (past[4], 3, 100),
            (pset, 2, 50), (pset, 1, 40), (essay, 1, 45), (project, 3, 60), (project, 2, 50), (labReport, 1, 50),
            (midterm, 4, 30), (midterm, 12, 25), (bioQuiz, 13, 30)
        };
        foreach (var (task, ago, minutes) in history)
        {
            data.Logs.Add(new StudyLog
            {
                TaskId = task.Id, SubjectId = task.SubjectId, Date = today.AddDays(-ago),
                Minutes = minutes + rng.Next(-5, 6), Source = "focus"
            });
        }

        var cards = new (Subject s, string f, string b)[]
        {
            (math, "d/dx [sin x]", "cos x"),
            (math, "∫ u dv =", "uv − ∫ v du"),
            (math, "d/dx [ln x]", "1/x"),
            (math, "Fundamental theorem of calculus (part 2)", "∫ₐᵇ f(x) dx = F(b) − F(a), where F′ = f"),
            (bio, "Michaelis constant Km", "Substrate concentration at which the reaction rate is half of Vmax"),
            (bio, "Second messenger produced by adenylyl cyclase", "cAMP"),
            (bio, "Where does glycolysis occur?", "In the cytoplasm"),
            (hist, "Year of the storming of the Bastille", "1789"),
            (hist, "Estates-General: who formed the Third Estate?", "Commoners — everyone who wasn't clergy or nobility"),
            (cs, "Time complexity of inserting at the head of a singly linked list", "O(1)"),
            (cs, "What does a stack overflow in recursion usually indicate?", "A missing or unreachable base case (or recursion that is too deep)"),
        };
        var i = 0;
        foreach (var (s, f, b) in cards)
        {
            data.Cards.Add(new Flashcard
            {
                SubjectId = s.Id, Front = f, Back = b,
                DueDate = today.AddDays(i++ % 3 == 0 ? 0 : i % 4),
                Repetitions = i % 3, IntervalDays = i % 4
            });
        }

        return data;
    }
}
