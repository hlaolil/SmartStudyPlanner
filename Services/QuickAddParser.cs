using System.Globalization;
using System.Text.RegularExpressions;
using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

public record QuickAddResult(
    string Title,
    Guid? SubjectId,
    TaskKind Kind,
    Priority Priority,
    DateOnly DueDate,
    double EstimatedHours,
    bool DueWasParsed,
    bool HoursWereParsed);

/// <summary>
/// Parses quick-add text such as
/// "Calc problem set 4 #math due fri 3h !high" or "Bio midterm dec 12 6h".
/// </summary>
public static partial class QuickAddParser
{
    private static readonly string[] Months =
        { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new()
    {
        ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday, ["tues"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday, ["weds"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday, ["thur"] = DayOfWeek.Thursday, ["thurs"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
    };

    public static double DefaultHours(TaskKind kind) => kind switch
    {
        TaskKind.Exam => 6,
        TaskKind.Project => 8,
        TaskKind.Assignment => 3,
        TaskKind.Reading => 1.5,
        _ => 2
    };

    public static QuickAddResult Parse(string input, IEnumerable<Subject> subjects, DateOnly today)
    {
        var text = " " + (input ?? "").Trim() + " ";

        // --- Subject: "#tag" (prefix match, spaces ignored) or a plain word equal to a subject name.
        Guid? subjectId = null;
        var subjectList = subjects.Where(s => !s.Archived && !string.IsNullOrWhiteSpace(s.Name)).ToList();
        var tag = TagRegex().Match(text);
        if (tag.Success)
        {
            var key = tag.Groups[1].Value.ToLowerInvariant();
            var match = subjectList.FirstOrDefault(s => Squash(s.Name) == key)
                        ?? subjectList.FirstOrDefault(s => Squash(s.Name).StartsWith(key, StringComparison.Ordinal));
            if (match != null)
            {
                subjectId = match.Id;
                text = text.Remove(tag.Index, tag.Length).Insert(tag.Index, " ");
            }
        }
        if (subjectId == null)
        {
            foreach (var s in subjectList.Where(s => s.Name.Trim().Length >= 3).OrderByDescending(s => s.Name.Length))
            {
                if (Regex.IsMatch(text, $@"(?<![\w]){Regex.Escape(s.Name.Trim())}(?![\w])", RegexOptions.IgnoreCase))
                {
                    subjectId = s.Id;
                    break;
                }
            }
        }

        // --- Priority
        var priority = Priority.Medium;
        var pr = PriorityRegex().Match(text);
        if (pr.Success)
        {
            var v = pr.Groups[1].Value.ToLowerInvariant();
            priority = v switch
            {
                "!!!" or "!high" or "!h" or "!hi" or "urgent" or "important" or "asap" => Priority.High,
                "!low" or "!l" or "!lo" => Priority.Low,
                _ => Priority.Medium
            };
            text = text.Remove(pr.Index, pr.Length).Insert(pr.Index, " ");
        }

        // --- Duration: "3h", "1.5 hrs", "90m", "1h30m"
        double? hours = null;
        var dur = DurationRegex().Match(text);
        if (dur.Success)
        {
            double h = 0;
            if (dur.Groups["h"].Success) h += double.Parse(dur.Groups["h"].Value, CultureInfo.InvariantCulture);
            if (dur.Groups["m"].Success) h += double.Parse(dur.Groups["m"].Value, CultureInfo.InvariantCulture) / 60.0;
            if (h > 0)
            {
                hours = Math.Round(h, 2);
                text = text.Remove(dur.Index, dur.Length).Insert(dur.Index, " ");
            }
        }

        // --- Due date
        DateOnly? due = null;
        foreach (var (regex, resolver) in DateRules())
        {
            var m = regex.Match(text);
            if (!m.Success) continue;
            var d = resolver(m, today);
            if (d is null) continue;
            due = d;
            text = text.Remove(m.Index, m.Length).Insert(m.Index, " ");
            break;
        }

        // --- Kind from keywords (kept in the title).
        var lower = text.ToLowerInvariant();
        var kind = TaskKind.Other;
        if (Regex.IsMatch(lower, @"\b(exam|test|quiz|midterm|final|finals|oral)\b")) kind = TaskKind.Exam;
        else if (Regex.IsMatch(lower, @"\b(project|presentation|thesis|capstone|portfolio)\b")) kind = TaskKind.Project;
        else if (Regex.IsMatch(lower, @"\b(read|reading|chapter|chapters|ch\.?|textbook|article)\b")) kind = TaskKind.Reading;
        else if (Regex.IsMatch(lower, @"\b(essay|assignment|homework|hw|lab|report|problem set|pset|worksheet|paper|exercise|exercises)\b")) kind = TaskKind.Assignment;

        var title = Regex.Replace(text, @"\s+", " ").Trim();
        title = Regex.Replace(title, @"(\s+(due|by|on|for|at))+$", "", RegexOptions.IgnoreCase).Trim();
        title = Regex.Replace(title, @"^(due|by)\s+", "", RegexOptions.IgnoreCase).Trim();
        if (title.Length > 0) title = char.ToUpperInvariant(title[0]) + title[1..];
        if (title.Length == 0) title = "Untitled task";

        return new QuickAddResult(
            title,
            subjectId,
            kind,
            priority,
            due ?? today.AddDays(7),
            hours ?? DefaultHours(kind),
            due.HasValue,
            hours.HasValue);
    }

    private static string Squash(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    private static IEnumerable<(Regex, Func<Match, DateOnly, DateOnly?>)> DateRules()
    {
        const string pre = @"(?<=\s)(?:(?:due|by|on)\s+)?";
        const string post = @"(?=\s)";

        yield return (new Regex(pre + @"(\d{4})-(\d{1,2})-(\d{1,2})" + post, RegexOptions.IgnoreCase), (m, _) =>
            TryDate(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));

        yield return (new Regex(pre + @"(today|tonight|tdy)" + post, RegexOptions.IgnoreCase), (_, t) => t);
        yield return (new Regex(pre + @"(tomorrow|tmrw|tmr|tom)" + post, RegexOptions.IgnoreCase), (_, t) => t.AddDays(1));

        yield return (new Regex(pre + @"in\s+(\d{1,3})\s*(d|day|days|w|wk|wks|week|weeks)" + post, RegexOptions.IgnoreCase), (m, t) =>
        {
            var n = int.Parse(m.Groups[1].Value);
            return m.Groups[2].Value.StartsWith("w", StringComparison.OrdinalIgnoreCase) ? t.AddDays(n * 7) : t.AddDays(n);
        });

        yield return (new Regex(pre + @"(next\s+week)" + post, RegexOptions.IgnoreCase), (_, t) => t.AddDays(7));
        yield return (new Regex(pre + @"(end\s+of\s+(the\s+)?week|eow)" + post, RegexOptions.IgnoreCase), (_, t) => NextWeekday(t, DayOfWeek.Friday, true));
        yield return (new Regex(pre + @"(end\s+of\s+(the\s+)?month|eom)" + post, RegexOptions.IgnoreCase), (_, t) =>
            new DateOnly(t.Year, t.Month, DateTime.DaysInMonth(t.Year, t.Month)));

        yield return (new Regex(pre + @"(next\s+)?(" + string.Join("|", Weekdays.Keys.OrderByDescending(k => k.Length)) + @")" + post, RegexOptions.IgnoreCase), (m, t) =>
        {
            var day = Weekdays[m.Groups[2].Value.ToLowerInvariant()];
            var d = NextWeekday(t, day, false);
            return m.Groups[1].Success ? d.AddDays(7) : d;
        });

        var monthAlt = @"(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?";
        yield return (new Regex(pre + monthAlt + @"\s+(\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(\d{4}))?" + post, RegexOptions.IgnoreCase), (m, t) =>
            ResolveMonthDay(MonthIndex(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null, t));
        yield return (new Regex(pre + @"(\d{1,2})(?:st|nd|rd|th)?\s+(?:of\s+)?" + monthAlt + @"(?:,?\s+(\d{4}))?" + post, RegexOptions.IgnoreCase), (m, t) =>
            ResolveMonthDay(MonthIndex(m.Groups[2].Value), int.Parse(m.Groups[1].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null, t));
    }

    private static int MonthIndex(string s) =>
        Array.IndexOf(Months, s.ToLowerInvariant()[..3]) + 1;

    private static DateOnly? ResolveMonthDay(int month, int day, int? year, DateOnly today)
    {
        if (month < 1) return null;
        if (year is { } y) return TryDate(y, month, day);
        var d = TryDate(today.Year, month, day);
        if (d is { } dd && dd < today) d = TryDate(today.Year + 1, month, day);
        return d;
    }

    private static DateOnly? TryDate(int y, int m, int d) =>
        m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m) ? new DateOnly(y, m, d) : null;

    /// <summary>Next occurrence of <paramref name="day"/> after today (or today when allowed).</summary>
    public static DateOnly NextWeekday(DateOnly today, DayOfWeek day, bool includeToday)
    {
        var diff = ((int)day - (int)today.DayOfWeek + 7) % 7;
        if (diff == 0 && !includeToday) diff = 7;
        return today.AddDays(diff);
    }

    [GeneratedRegex(@"(?<=\s)#([\p{L}\p{N}_-]+)(?=\s)")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(?<=\s)(!!!|!high|!hi|!h|!low|!lo|!l|!med|!medium|!m|urgent|important|asap)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex PriorityRegex();

    [GeneratedRegex(@"(?<=\s)(?:~|about\s+)?(?:(?<h>\d+(?:\.\d+)?)\s*(?:h|hr|hrs|hour|hours)(?:\s*(?<m>\d{1,2})\s*(?:m|min|mins|minutes))?|(?<m>\d+)\s*(?:m|min|mins|minutes))(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();
}
