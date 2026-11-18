using SmartStudyPlanner.Models;

namespace SmartStudyPlanner.Services;

/// <summary>
/// SM-2 style spaced repetition with four answer buttons, plus exam awareness:
/// when the card's subject has an upcoming exam, intervals are capped so the
/// card is seen again before exam day.
/// </summary>
public static class SpacedRepetition
{
    public const double MinEase = 1.3;

    public static int PreviewInterval(Flashcard card, ReviewGrade grade, DateOnly today, DateOnly? nextExam = null)
    {
        var copy = new Flashcard
        {
            Ease = card.Ease, IntervalDays = card.IntervalDays, Repetitions = card.Repetitions,
            Lapses = card.Lapses, DueDate = card.DueDate
        };
        Apply(copy, grade, today, nextExam);
        return copy.IntervalDays;
    }

    public static void Apply(Flashcard card, ReviewGrade grade, DateOnly today, DateOnly? nextExam = null)
    {
        int interval;
        if (grade == ReviewGrade.Again)
        {
            card.Repetitions = 0;
            card.Lapses++;
            card.Ease = Math.Max(MinEase, card.Ease - 0.2);
            interval = 0; // see it again today
        }
        else
        {
            interval = card.Repetitions switch
            {
                0 => grade switch { ReviewGrade.Hard => 1, ReviewGrade.Good => 1, _ => 3 },
                1 => grade switch { ReviewGrade.Hard => 2, ReviewGrade.Good => 4, _ => 6 },
                _ => grade switch
                {
                    ReviewGrade.Hard => (int)Math.Ceiling(Math.Max(1, card.IntervalDays) * 1.2),
                    ReviewGrade.Good => (int)Math.Ceiling(Math.Max(1, card.IntervalDays) * card.Ease),
                    _ => (int)Math.Ceiling(Math.Max(1, card.IntervalDays) * card.Ease * 1.3)
                }
            };
            card.Ease = grade switch
            {
                ReviewGrade.Hard => Math.Max(MinEase, card.Ease - 0.15),
                ReviewGrade.Easy => card.Ease + 0.15,
                _ => card.Ease
            };
            card.Repetitions++;

            if (nextExam is { } exam && exam > today)
            {
                // Make sure there is at least one more review before the exam.
                var daysToExam = exam.DayNumber - today.DayNumber;
                var cap = Math.Max(1, daysToExam / 2);
                if (daysToExam > 1) interval = Math.Min(interval, cap);
            }
            interval = Math.Clamp(interval, 1, 365);
        }

        card.IntervalDays = interval;
        card.DueDate = today.AddDays(interval);
        card.LastReviewed = today;
    }

    public static string Describe(int days) => days switch
    {
        0 => "<10m",
        1 => "1d",
        < 30 => $"{days}d",
        < 365 => $"{Math.Round(days / 30.0, 1)}mo",
        _ => $"{Math.Round(days / 365.0, 1)}y"
    };
}
