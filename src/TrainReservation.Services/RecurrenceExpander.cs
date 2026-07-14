using TrainReservation.Domain;

namespace TrainReservation.Services;

/// <summary>
/// Turns a <see cref="RecurrencePattern"/> into the dates it actually falls on.
///
/// Occurrences are generated on demand and never stored: a commute repeating every Monday for a
/// year is one Booking plus a pattern, not fifty-two rows. The weekly view asks for just the
/// window it is showing.
/// </summary>
public static class RecurrenceExpander
{
    /// <summary>
    /// Every date the pattern falls on inside [<paramref name="from"/>, <paramref name="to"/>],
    /// in ascending order. Dates outside the pattern's own start/end are never produced.
    /// </summary>
    public static IEnumerable<DateOnly> Expand(RecurrencePattern pattern, DateOnly from, DateOnly to)
    {
        // Clip the requested window to the pattern's own lifetime.
        var start = from > pattern.StartDate ? from : pattern.StartDate;
        var end = to < pattern.EndDate ? to : pattern.EndDate;

        if (start > end) yield break;

        var interval = Math.Max(1, pattern.Interval);

        switch (pattern.Frequency)
        {
            case Frequency.Daily:
                // Step in whole intervals from the pattern's start so the phase stays correct even
                // when the requested window begins mid-series.
                var daysIn = (start.DayNumber - pattern.StartDate.DayNumber + interval - 1) / interval;
                var daily = pattern.StartDate.AddDays(daysIn * interval);

                for (var date = daily; date <= end; date = date.AddDays(interval))
                    if (date >= start)
                        yield return date;

                break;

            case Frequency.Weekly:
                // Weeks are counted from the Monday of the pattern's start week, so "every 2nd week"
                // means the same alternating weeks regardless of which day the series began on.
                var anchorWeek = StartOfWeek(pattern.StartDate);

                for (var date = start; date <= end; date = date.AddDays(1))
                {
                    // A weekly pattern with no days chosen falls back to the day it started on.
                    var days = pattern.DaysOfWeek.Length > 0
                        ? pattern.DaysOfWeek
                        : new[] { pattern.StartDate.DayOfWeek };

                    if (!days.Contains(date.DayOfWeek)) continue;

                    var weeksApart = (StartOfWeek(date).DayNumber - anchorWeek.DayNumber) / 7;
                    if (weeksApart % interval == 0)
                        yield return date;
                }

                break;

            case Frequency.Monthly:
                // Same day of the month as the start date, every N months. Months that are too short
                // (a 31st in February) are skipped rather than silently shifted into the next month.
                var day = pattern.StartDate.Day;
                var cursor = new DateOnly(pattern.StartDate.Year, pattern.StartDate.Month, 1);

                while (true)
                {
                    var monthEnd = cursor.AddMonths(1).AddDays(-1);
                    if (cursor > end && monthEnd > end) break;

                    if (day <= DateTime.DaysInMonth(cursor.Year, cursor.Month))
                    {
                        var candidate = new DateOnly(cursor.Year, cursor.Month, day);
                        if (candidate >= start && candidate <= end)
                            yield return candidate;

                        if (candidate > end) break;
                    }

                    cursor = cursor.AddMonths(interval);
                }

                break;
        }
    }

    /// <summary>Monday of the week containing <paramref name="date"/>.</summary>
    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-offset);
    }
}
