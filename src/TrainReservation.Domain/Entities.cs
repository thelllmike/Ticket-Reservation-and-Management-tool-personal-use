using System.ComponentModel.DataAnnotations;

namespace TrainReservation.Domain;

/// <summary>
/// Anything stored in an id-keyed dictionary. Lets the in-memory repositories share
/// one generic implementation instead of repeating CRUD per entity.
/// </summary>
public interface IEntity
{
    int Id { get; set; }
}

/// <summary>
/// The traveller. This iteration has no authentication, so a single personal
/// user is seeded and every booking belongs to them.
/// </summary>
public class User : IEntity
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// A station on the network. <see cref="Code"/> is the natural key (e.g. "LDS"),
/// so the station store is keyed by code rather than an integer id.
/// </summary>
public class Station
{
    [Required, StringLength(4, MinimumLength = 3)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string City { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({Code})";
}

/// <summary>
/// An origin-to-destination pair. Distance drives the base fare, so pricing stays
/// derivable from the domain rather than being hard-coded per service.
/// </summary>
public class Route : IEntity
{
    public int Id { get; set; }

    [Required]
    public Station OriginStation { get; set; } = new();

    [Required]
    public Station DestinationStation { get; set; } = new();

    [Range(1, 2000)]
    public int DistanceKm { get; set; }

    /// <summary>Display label used across the schedule, reports and chatbot replies.</summary>
    public string Description => $"{OriginStation.Name} → {DestinationStation.Name}";

    /// <summary>
    /// City-level label ("London → Manchester"). The full station names do not fit in the narrow
    /// columns of the weekly grid, where truncating would hide the destination — the one thing the
    /// user most needs to see.
    /// </summary>
    public string ShortDescription => $"{OriginStation.City} → {DestinationStation.City}";
}

/// <summary>
/// A scheduled train on a route. This is the "Schedule" entity the user performs CRUD on.
/// </summary>
public class TrainService : IEntity
{
    public int Id { get; set; }

    [Required, StringLength(10)]
    public string TrainNumber { get; set; } = string.Empty;

    public int RouteId { get; set; }

    public TimeOnly DepartureTime { get; set; }

    public TimeOnly ArrivalTime { get; set; }

    /// <summary>Days of the week this service actually runs.</summary>
    public DayOfWeek[] OperatingDays { get; set; } = Array.Empty<DayOfWeek>();

    /// <summary>
    /// Total seats on the train. Not in the original brief, but occupancy
    /// (seats sold / capacity) is what the availability prediction is built on.
    /// </summary>
    [Range(10, 1000)]
    public int Capacity { get; set; } = 120;

    /// <summary>Navigation property, resolved by the service layer.</summary>
    public Route? Route { get; set; }

    public bool RunsOn(DateOnly date) => OperatingDays.Contains(date.DayOfWeek);
}

/// <summary>
/// A reservation. Acts as the aggregate root: it owns its <see cref="Seats"/> and
/// <see cref="SpecialRequests"/>, which are indexed separately for O(1) lookup by id.
/// </summary>
public class Booking : IEntity
{
    public int Id { get; set; }

    /// <summary>Human-facing reference, e.g. "TKT-10042".</summary>
    public string Reference { get; set; } = string.Empty;

    public DateTime DateCreated { get; set; } = DateTime.Now;

    public DateOnly TravelDate { get; set; }

    public int TrainServiceId { get; set; }

    public int UserId { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public bool IsRecurring { get; set; }

    public decimal TotalPrice { get; set; }

    public List<Seat> Seats { get; set; } = new();

    public List<SpecialRequest> SpecialRequests { get; set; } = new();

    /// <summary>Set only when <see cref="IsRecurring"/> is true.</summary>
    public RecurrencePattern? RecurrencePattern { get; set; }

    /// <summary>
    /// When the user edits or cancels a single occurrence of a recurring booking, that
    /// occurrence is materialised into its own one-off Booking pointing back at the
    /// parent series. Expansion then skips the parent's occurrence on that date.
    /// </summary>
    public int? ParentBookingId { get; set; }

    /// <summary>Navigation property, resolved by the service layer.</summary>
    public TrainService? TrainService { get; set; }

    /// <summary>Recomputed from the owned seats so price can never drift from the seats sold.</summary>
    public decimal CalculateTotal() => Seats.Sum(s => s.Price);
}

/// <summary>A single reserved seat. Owned by a <see cref="Booking"/>.</summary>
public class Seat : IEntity
{
    public int Id { get; set; }

    public int BookingId { get; set; }

    [Required, StringLength(2)]
    public string Coach { get; set; } = "A";

    [Range(1, 200)]
    public int SeatNumber { get; set; }

    public TravelClass TravelClass { get; set; } = TravelClass.Standard;

    [Range(0, 10000)]
    public decimal Price { get; set; }

    /// <summary>Identity of the physical seat, used to detect double-booking.</summary>
    public string Label => $"{Coach}{SeatNumber}";
}

/// <summary>An assistance request attached to a booking.</summary>
public class SpecialRequest : IEntity
{
    public int Id { get; set; }

    public int BookingId { get; set; }

    public RequestType Type { get; set; }

    [StringLength(250)]
    public string Description { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Requested;
}

/// <summary>
/// Describes how a recurring booking repeats. Occurrences are generated on demand
/// (never stored) unless the user materialises one.
/// </summary>
public class RecurrencePattern : IEntity
{
    public int Id { get; set; }

    public Frequency Frequency { get; set; } = Frequency.Weekly;

    /// <summary>Repeat every N days/weeks/months.</summary>
    [Range(1, 12)]
    public int Interval { get; set; } = 1;

    /// <summary>Only meaningful for <see cref="Frequency.Weekly"/>.</summary>
    public DayOfWeek[] DaysOfWeek { get; set; } = Array.Empty<DayOfWeek>();

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
