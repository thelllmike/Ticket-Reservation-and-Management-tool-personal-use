using System.Globalization;
using System.Text.RegularExpressions;
using TrainReservation.Domain;

namespace TrainReservation.Services;

/// <summary>What the parser managed to pull out of a free-text question.</summary>
public class ParsedQuestion
{
    public Station? Origin { get; set; }
    public Station? Destination { get; set; }
    public DateOnly? Date { get; set; }
    public TimeOnly? Time { get; set; }

    /// <summary>True when the message mentioned no date at all, so the caller has to assume one.</summary>
    public bool DateWasAssumed { get; set; }

    /// <summary>Whether the user asked mainly about price, seats, or did not say.</summary>
    public bool AsksAboutPrice { get; set; }

    public bool AsksAboutAvailability { get; set; }
}

/// <summary>
/// Pulls a route, a date and optionally a departure time out of a question like
/// "What will a London→Manchester ticket cost next Friday?".
///
/// Deliberately keyword and regex based: the brief calls for deterministic, offline parsing, and a
/// handful of patterns covers the phrasings people actually use for this.
/// </summary>
public class QuestionParser
{
    private readonly IReadOnlyCollection<Station> _stations;

    public QuestionParser(IReadOnlyCollection<Station> stations) => _stations = stations;

    private static readonly string[] PriceWords =
        { "cost", "price", "fare", "cheap", "expensive", "pay", "£", "pricing", "spend" };

    private static readonly string[] AvailabilityWords =
        { "seat", "seats", "available", "availability", "full", "busy", "space", "book", "room", "capacity" };

    public ParsedQuestion Parse(string question)
    {
        var text = (question ?? string.Empty).ToLowerInvariant();

        var result = new ParsedQuestion
        {
            AsksAboutPrice = PriceWords.Any(text.Contains),
            AsksAboutAvailability = AvailabilityWords.Any(text.Contains)
        };

        ParseStations(text, result);
        ParseDate(text, result);
        ParseTime(text, result);

        return result;
    }

    // -----------------------------------------------------------------------------------------
    // Stations
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Finds every station the message mentions, then decides which is the origin and which the
    /// destination from where they sit relative to the word "to".
    /// </summary>
    private void ParseStations(string text, ParsedQuestion result)
    {
        // Each station can be named by its city ("London"), its full name, or its code ("KGX").
        // Longest match wins, so "London King's Cross" is not mistaken for plain "London".
        var mentions = new List<(Station Station, int Index, int Length)>();

        foreach (var station in _stations)
        {
            var candidates = new[] { station.Name.ToLowerInvariant(), station.City.ToLowerInvariant(), station.Code.ToLowerInvariant() };

            foreach (var candidate in candidates)
            {
                var index = FindWord(text, candidate);
                if (index >= 0)
                    mentions.Add((station, index, candidate.Length));
            }
        }

        // Keep the best (longest) mention per station, then read them left to right.
        var ordered = mentions
            .GroupBy(m => m.Station.Code)
            .Select(g => g.OrderByDescending(m => m.Length).First())
            .OrderBy(m => m.Index)
            .ToList();

        if (ordered.Count == 0) return;

        if (ordered.Count == 1)
        {
            // "seats to Leeds" names only one end. Whether it is the origin or the destination
            // depends on the preposition in front of it; "to" is by far the common case.
            var only = ordered[0];
            var before = text[..only.Index];

            if (before.TrimEnd().EndsWith("from"))
                result.Origin = only.Station;
            else
                result.Destination = only.Station;

            return;
        }

        // Two or more: the one before "to" is the origin, the one after is the destination.
        // Falls back to plain left-to-right order, which is what "London Manchester" implies anyway.
        var toIndex = FindWord(text, "to");

        if (toIndex > 0)
        {
            var origin = ordered.LastOrDefault(m => m.Index < toIndex);
            var destination = ordered.FirstOrDefault(m => m.Index > toIndex);

            if (origin.Station is not null && destination.Station is not null)
            {
                result.Origin = origin.Station;
                result.Destination = destination.Station;
                return;
            }
        }

        result.Origin = ordered[0].Station;
        result.Destination = ordered[1].Station;
    }

    /// <summary>
    /// Index of <paramref name="word"/> in <paramref name="text"/> as a whole word, or -1.
    /// Whole-word matching stops "york" matching inside "new york" style substrings, and more
    /// importantly stops the station code "man" matching the word "many".
    /// </summary>
    private static int FindWord(string text, string word)
    {
        var match = Regex.Match(text, $@"(?<![\w']){Regex.Escape(word)}(?![\w'])");
        return match.Success ? match.Index : -1;
    }

    // -----------------------------------------------------------------------------------------
    // Dates
    // -----------------------------------------------------------------------------------------

    private static void ParseDate(string text, ParsedQuestion result)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (FindWord(text, "today") >= 0)
        {
            result.Date = today;
            return;
        }

        if (FindWord(text, "tomorrow") >= 0)
        {
            result.Date = today.AddDays(1);
            return;
        }

        // A named day: "next friday", "this friday", "on friday", or a bare "friday".
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var name = day.ToString().ToLowerInvariant();
            if (FindWord(text, name) < 0) continue;

            result.Date = NextOccurrenceOf(today, day);
            return;
        }

        if (text.Contains("next week"))
        {
            result.Date = RecurrenceExpander.StartOfWeek(today).AddDays(7);
            return;
        }

        // Explicit dates: 2026-08-12, 12/08/2026, "12 August", "August 12".
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy" };

        var numeric = Regex.Match(text, @"\b(\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[/-]\d{1,2}[/-]\d{4})\b");
        if (numeric.Success &&
            DateTime.TryParseExact(numeric.Value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            result.Date = DateOnly.FromDateTime(parsed);
            return;
        }

        // "12 August" / "August 12", with the year implied — if the day has already passed this
        // year, the user must mean next year.
        var textual = Regex.Match(text, @"\b(\d{1,2})\s+(january|february|march|april|may|june|july|august|september|october|november|december)\b")
                      is { Success: true } m1 ? $"{m1.Groups[1].Value} {m1.Groups[2].Value}"
                      : Regex.Match(text, @"\b(january|february|march|april|may|june|july|august|september|october|november|december)\s+(\d{1,2})\b")
                      is { Success: true } m2 ? $"{m2.Groups[2].Value} {m2.Groups[1].Value}"
                      : null;

        if (textual is not null &&
            DateTime.TryParseExact($"{textual} {today.Year}", new[] { "d MMMM yyyy", "d MMM yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dayMonth))
        {
            var date = DateOnly.FromDateTime(dayMonth);
            result.Date = date < today ? date.AddYears(1) : date;
            return;
        }

        // Nothing said about when — the caller assumes a date and tells the user it did so.
        result.Date = today.AddDays(1);
        result.DateWasAssumed = true;
    }

    /// <summary>The next <paramref name="day"/> strictly after today (so "Friday" on a Friday means next week's).</summary>
    private static DateOnly NextOccurrenceOf(DateOnly today, DayOfWeek day)
    {
        var offset = ((int)day - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(offset == 0 ? 7 : offset);
    }

    // -----------------------------------------------------------------------------------------
    // Times
    // -----------------------------------------------------------------------------------------

    private static void ParseTime(string text, ParsedQuestion result)
    {
        // "08:00" / "8:00"
        var clock = Regex.Match(text, @"\b(\d{1,2}):(\d{2})\b");
        if (clock.Success &&
            int.TryParse(clock.Groups[1].Value, out var h) &&
            int.TryParse(clock.Groups[2].Value, out var m) &&
            h < 24 && m < 60)
        {
            result.Time = new TimeOnly(h, m);
            return;
        }

        // "8am" / "5 pm"
        var meridiem = Regex.Match(text, @"\b(\d{1,2})\s*(am|pm)\b");
        if (meridiem.Success && int.TryParse(meridiem.Groups[1].Value, out var hour) && hour is >= 1 and <= 12)
        {
            var pm = meridiem.Groups[2].Value == "pm";
            if (pm && hour != 12) hour += 12;
            if (!pm && hour == 12) hour = 0;

            result.Time = new TimeOnly(hour, 0);
        }
    }
}
