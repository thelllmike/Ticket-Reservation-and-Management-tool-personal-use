using TrainReservation.Domain;

namespace TrainReservation.Services.Models;

/// <summary>
/// One dated instance of a booking. A one-off booking yields exactly one; a recurring series
/// yields one per matching date, generated on demand rather than stored.
/// </summary>
public class BookingOccurrence
{
    public required Booking Booking { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>True when this date came from expanding a recurrence pattern.</summary>
    public bool IsGenerated { get; init; }

    /// <summary>
    /// True when this occurrence has been split out of its series into a Booking of its own
    /// (because the user edited or cancelled just that date). <see cref="Booking"/> is then the
    /// materialised child, not the parent series.
    /// </summary>
    public bool IsMaterialised { get; init; }

    /// <summary>The series this occurrence belongs to, when it was generated from one.</summary>
    public int? SeriesId { get; init; }
}

/// <summary>Outcome of an operation that can fail validation, so controllers can surface why.</summary>
public class OperationResult
{
    public bool Success { get; private init; }
    public List<string> Errors { get; private init; } = new();
    public int? EntityId { get; private init; }

    public static OperationResult Ok(int? entityId = null) => new() { Success = true, EntityId = entityId };

    public static OperationResult Fail(params string[] errors) => new() { Success = false, Errors = errors.ToList() };

    public static OperationResult Fail(IEnumerable<string> errors) => new() { Success = false, Errors = errors.ToList() };
}

// ---------------------------------------------------------------------------------------------
// Weekly view / report
// ---------------------------------------------------------------------------------------------

/// <summary>A single day within the Monday–Sunday grid.</summary>
public class WeeklyDay
{
    public DateOnly Date { get; init; }
    public List<BookingOccurrence> Occurrences { get; init; } = new();

    public IEnumerable<SpecialRequest> SpecialRequests =>
        Occurrences.SelectMany(o => o.Booking.SpecialRequests);

    public decimal TotalSpend => Occurrences
        .Where(o => o.Booking.Status != BookingStatus.Cancelled)
        .Sum(o => o.Booking.TotalPrice);

    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>A Monday–Sunday window, used by both the weekly view and the weekly report.</summary>
public class WeeklyView
{
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd => WeekStart.AddDays(6);
    public List<WeeklyDay> Days { get; init; } = new();

    public int TotalBookings => Days.Sum(d => d.Occurrences.Count);
    public int TotalSpecialRequests => Days.Sum(d => d.SpecialRequests.Count());
    public decimal TotalSpend => Days.Sum(d => d.TotalSpend);

    /// <summary>Distinct routes travelled this week, for the report summary.</summary>
    public IEnumerable<string> RoutesTravelled => Days
        .SelectMany(d => d.Occurrences)
        .Select(o => o.Booking.TrainService?.Route?.Description)
        .Where(d => d is not null)
        .Distinct()!
        .Cast<string>();
}

// ---------------------------------------------------------------------------------------------
// Prediction
// ---------------------------------------------------------------------------------------------

public enum AvailabilityLevel
{
    LikelyAvailable,
    Limited,
    LikelyFull
}

public enum TrendDirection
{
    Rising,
    Stable,
    Easing
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// A prediction for one route on one date, plus the figures that back it up. The chatbot renders
/// this as prose; keeping the numbers alongside means the UI can show its working.
/// </summary>
public class Prediction
{
    public Route? Route { get; init; }
    public DateOnly TargetDate { get; init; }
    public TrainService? Service { get; init; }

    // Pricing
    public decimal ProjectedPrice { get; init; }
    public decimal RecentAveragePrice { get; init; }
    public decimal OlderAveragePrice { get; init; }
    public TrendDirection Trend { get; init; }
    public decimal TrendPercent { get; init; }

    // Availability
    public AvailabilityLevel Availability { get; init; }
    public double AverageOccupancy { get; init; }
    public int TypicalSeatsFree { get; init; }

    // Evidence
    public int SampleSize { get; init; }
    public ConfidenceLevel Confidence { get; init; }
}

/// <summary>What the chatbot sends back: prose plus, when it managed to predict, the figures.</summary>
public class ChatResponse
{
    public required string Message { get; init; }

    /// <summary>Null when the bot had to ask a clarifying question instead of answering.</summary>
    public Prediction? Prediction { get; init; }

    /// <summary>True when the message is a request for more information rather than an answer.</summary>
    public bool NeedsClarification { get; init; }
}
