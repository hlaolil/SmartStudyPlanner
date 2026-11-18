using System.Text;
using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

/// <summary>Exports planned study blocks and deadlines as an iCalendar (.ics) file.</summary>
public static class IcsExporter
{
    public static string Build(SchedulePlan plan, AppData data, int days = 14)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//SmartStudyPlanner//EN\r\nCALSCALE:GREGORIAN\r\nX-WR-CALNAME:Study plan\r\n");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = plan.Today.AddDays(days);
        var subjects = data.Subjects.ToDictionary(s => s.Id);

        foreach (var b in plan.Blocks.Where(b => b.Date < end))
        {
            var subject = b.SubjectId is { } sid && subjects.TryGetValue(sid, out var s) ? s.Name : null;
            var start = b.Date.ToDateTime(b.Start);
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{b.Date:yyyyMMdd}-{b.Start:HHmm}-{b.TaskId?.ToString("N") ?? "review"}@smartstudyplanner\r\n");
            sb.Append($"DTSTAMP:{stamp}\r\n");
            sb.Append($"DTSTART:{start:yyyyMMdd'T'HHmmss}\r\n");
            sb.Append($"DTEND:{start.AddMinutes(b.Minutes):yyyyMMdd'T'HHmmss}\r\n");
            sb.Append(Fold($"SUMMARY:{Escape((subject is null ? "" : subject + ": ") + b.Title)}"));
            sb.Append("END:VEVENT\r\n");
        }

        foreach (var t in data.Tasks.Where(t => !t.Completed && t.DueDate >= plan.Today && t.DueDate < end.AddDays(60)))
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:due-{t.Id:N}@smartstudyplanner\r\n");
            sb.Append($"DTSTAMP:{stamp}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{t.DueDate:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{t.DueDate.AddDays(1):yyyyMMdd}\r\n");
            sb.Append(Fold($"SUMMARY:{Escape("Due: " + t.Title)}"));
            sb.Append("END:VEVENT\r\n");
        }

        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    private static string Fold(string line)
    {
        var sb = new StringBuilder();
        while (line.Length > 73)
        {
            sb.Append(line[..73]).Append("\r\n ");
            line = line[73..];
        }
        sb.Append(line).Append("\r\n");
        return sb.ToString();
    }
}
