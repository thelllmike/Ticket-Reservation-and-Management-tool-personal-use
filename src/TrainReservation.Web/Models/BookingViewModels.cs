using System.ComponentModel.DataAnnotations;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Web.Models;

/// <summary>One seat row posted back from the seat picker.</summary>
public class SeatInput
{
    [Required, StringLength(2)]
    public string Coach { get; set; } = "B";

    [Range(1, 200)]
    public int SeatNumber { get; set; }

    public TravelClass TravelClass { get; set; }
}

/// <summary>One special-request row posted back from the booking form.</summary>
public class SpecialRequestInput
{
    public RequestType Type { get; set; }

    [StringLength(250)]
    public string Description { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Requested;

    /// <summary>Set when editing an existing request, so it can be updated rather than replaced.</summary>
    public int Id { get; set; }
}

/// <summary>
/// Backs the create/edit booking form. Kept separate from the <see cref="Booking"/> entity because
/// the form works in terms the user understands (a route, a list of seats) while the entity works
/// in terms of ids and prices — and because prices must be computed server-side, never posted.
/// </summary>
public class BookingFormModel
{
    public int Id { get; set; }

    public string? Reference { get; set; }

    [Required(ErrorMessage = "Choose a route.")]
    [Range(1, int.MaxValue, ErrorMessage = "Choose a route.")]
    [Display(Name = "Route")]
    public int RouteId { get; set; }

    [Required(ErrorMessage = "Choose a travel date.")]
    [DataType(DataType.Date)]
    [Display(Name = "Travel date")]
    public DateOnly TravelDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

    [Required(ErrorMessage = "Choose a train service.")]
    [Range(1, int.MaxValue, ErrorMessage = "Choose a train service.")]
    [Display(Name = "Train service")]
    public int TrainServiceId { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    [Display(Name = "Repeat this booking")]
    public bool IsRecurring { get; set; }

    public List<SeatInput> Seats { get; set; } = new();

    public List<SpecialRequestInput> SpecialRequests { get; set; } = new();

    // ---- Recurrence, flattened so it binds cleanly from the form ----------------------------

    [Display(Name = "Repeats")]
    public Frequency Frequency { get; set; } = Frequency.Weekly;

    [Range(1, 12, ErrorMessage = "The interval must be between 1 and 12.")]
    [Display(Name = "Every")]
    public int Interval { get; set; } = 1;

    [Display(Name = "On these days")]
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    [DataType(DataType.Date)]
    [Display(Name = "Repeat from")]
    public DateOnly RecurrenceStart { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [DataType(DataType.Date)]
    [Display(Name = "Repeat until")]
    public DateOnly RecurrenceEnd { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddMonths(3);

    // ---- Populated by the controller for rendering -------------------------------------------

    public List<Route> Routes { get; set; } = new();
    public List<TrainService> Services { get; set; } = new();

    /// <summary>Seat labels already sold on the chosen service and date; the picker greys these out.</summary>
    public HashSet<string> TakenSeats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Capacity of the chosen service, so the picker knows how big a train to draw.</summary>
    public int Capacity { get; set; } = 110;

    public decimal StandardPrice { get; set; }
    public decimal FirstClassPrice { get; set; }

    public bool IsEdit => Id > 0;

    /// <summary>Maps the form back onto the domain entity. Prices are recomputed, never trusted from the post.</summary>
    public Booking ToBooking(int distanceKm) => new()
    {
        Id = Id,
        TrainServiceId = TrainServiceId,
        TravelDate = TravelDate,
        Status = Status,
        IsRecurring = IsRecurring,
        Seats = Seats.Select(s => new Seat
        {
            Coach = s.Coach,
            SeatNumber = s.SeatNumber,
            TravelClass = s.TravelClass,
            Price = PricingCalculator.SeatPrice(distanceKm, s.TravelClass)
        }).ToList(),
        SpecialRequests = SpecialRequests
            .Where(r => r.Type != default || !string.IsNullOrWhiteSpace(r.Description))
            .Select(r => new SpecialRequest
            {
                Type = r.Type,
                Description = r.Description,
                Status = r.Status
            }).ToList(),
        RecurrencePattern = IsRecurring
            ? new RecurrencePattern
            {
                Frequency = Frequency,
                Interval = Interval,
                DaysOfWeek = DaysOfWeek.ToArray(),
                StartDate = RecurrenceStart,
                EndDate = RecurrenceEnd
            }
            : null
    };

    /// <summary>Builds the form from an existing booking, for the edit screen.</summary>
    public static BookingFormModel FromBooking(Booking booking) => new()
    {
        Id = booking.Id,
        Reference = booking.Reference,
        RouteId = booking.TrainService?.RouteId ?? 0,
        TravelDate = booking.TravelDate,
        TrainServiceId = booking.TrainServiceId,
        Status = booking.Status,
        IsRecurring = booking.IsRecurring,
        Seats = booking.Seats.Select(s => new SeatInput
        {
            Coach = s.Coach,
            SeatNumber = s.SeatNumber,
            TravelClass = s.TravelClass
        }).ToList(),
        SpecialRequests = booking.SpecialRequests.Select(r => new SpecialRequestInput
        {
            Id = r.Id,
            Type = r.Type,
            Description = r.Description,
            Status = r.Status
        }).ToList(),
        Frequency = booking.RecurrencePattern?.Frequency ?? Frequency.Weekly,
        Interval = booking.RecurrencePattern?.Interval ?? 1,
        DaysOfWeek = booking.RecurrencePattern?.DaysOfWeek.ToList() ?? new List<DayOfWeek>(),
        RecurrenceStart = booking.RecurrencePattern?.StartDate ?? booking.TravelDate,
        RecurrenceEnd = booking.RecurrencePattern?.EndDate ?? booking.TravelDate.AddMonths(3)
    };
}

/// <summary>The Bookings index, split so upcoming travel is not buried under old trips.</summary>
public class BookingListViewModel
{
    public List<Booking> Upcoming { get; set; } = new();
    public List<Booking> Past { get; set; } = new();
    public List<Booking> Recurring { get; set; } = new();
}
