using SmartStudyPlanner.Models;
using SmartStudyPlanner.Services;

namespace SmartStudyPlanner.Tests;

public class QuickAddParserTests
{
    private static readonly DateOnly Wed = new(2026, 11, 18); // a Wednesday
    private static readonly Subject Bio = new() { Name = "Biology" };
    private static readonly Subject Hist = new() { Name = "World History" };
    private static readonly Subject[] Subjects = { Bio, Hist };

    private static QuickAddResult P(string s) => QuickAddParser.Parse(s, Subjects, Wed);

    [Fact]
    public void Parses_tag_weekday_hours_and_priority()
    {
        var r = P("Lab report #bio due fri 4h !high");
        Assert.Equal("Lab report", r.Title);
        Assert.Equal(Bio.Id, r.SubjectId);
        Assert.Equal(new DateOnly(2026, 11, 20), r.DueDate);
        Assert.Equal(4, r.EstimatedHours);
        Assert.Equal(Priority.High, r.Priority);
        Assert.Equal(TaskKind.Assignment, r.Kind);
        Assert.True(r.DueWasParsed && r.HoursWereParsed);
    }

    [Fact]
    public void Matches_subject_by_plain_name_and_keeps_it_in_title()
    {
        var r = P("Biology quiz tomorrow");
        Assert.Equal(Bio.Id, r.SubjectId);
        Assert.Equal("Biology quiz", r.Title);
        Assert.Equal(TaskKind.Exam, r.Kind);
        Assert.Equal(Wed.AddDays(1), r.DueDate);
    }

    [Fact]
    public void Multiword_subject_tag_matches_without_spaces()
    {
        Assert.Equal(Hist.Id, P("Essay #worldhistory").SubjectId);
        Assert.Equal(Hist.Id, P("Essay #world").SubjectId);
    }

    [Theory]
    [InlineData("Read ch 4 today", 0)]
    [InlineData("Read ch 4 tmrw", 1)]
    [InlineData("Paper in 3 days", 3)]
    [InlineData("Paper in 2 weeks", 14)]
    [InlineData("Paper next week", 7)]
    [InlineData("Paper wed", 7)] // same weekday means next week
    [InlineData("Paper next mon", 12)]
    [InlineData("Paper by thursday", 1)]
    public void Resolves_relative_dates(string text, int days) =>
        Assert.Equal(Wed.AddDays(days), P(text).DueDate);

    [Theory]
    [InlineData("Final dec 12", 2026, 12, 12)]
    [InlineData("Final 3rd of march", 2027, 3, 3)]
    [InlineData("Final Jan 5th", 2027, 1, 5)]
    [InlineData("Final 2027-02-01", 2027, 2, 1)]
    public void Resolves_absolute_dates(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), P(text).DueDate);

    [Theory]
    [InlineData("Essay 90m", 1.5)]
    [InlineData("Essay 1h30m", 1.5)]
    [InlineData("Essay 2.5 hours", 2.5)]
    public void Parses_durations(string text, double hours) =>
        Assert.Equal(hours, P(text).EstimatedHours, 2);

    [Fact]
    public void Uses_sensible_defaults()
    {
        var r = P("Midterm");
        Assert.Equal(TaskKind.Exam, r.Kind);
        Assert.Equal(Wed.AddDays(7), r.DueDate);
        Assert.Equal(QuickAddParser.DefaultHours(TaskKind.Exam), r.EstimatedHours);
        Assert.False(r.DueWasParsed);
    }

    [Fact]
    public void Does_not_mistake_words_for_dates()
    {
        var r = P("Essay on the french revolution");
        Assert.Equal("Essay on the french revolution", r.Title);
        Assert.False(r.DueWasParsed);
    }
}
