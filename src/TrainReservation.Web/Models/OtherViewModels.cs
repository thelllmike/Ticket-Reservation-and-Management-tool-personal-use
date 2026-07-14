using System.ComponentModel.DataAnnotations;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Web.Models;

/// <summary>Backs the create/edit screens for a scheduled train service.</summary>
public class TrainServiceFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a train number.")]
    [StringLength(10)]
    [Display(Name = "Train number")]
    public string TrainNumber { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Choose a route.")]
    [Display(Name = "Route")]
    public int RouteId { get; set; }

    [Required]
    [DataType(DataType.Time)]
    [Display(Name = "Departure")]
    public TimeOnly DepartureTime { get; set; } = new(8, 0);

    [Required]
    [DataType(DataType.Time)]
    [Display(Name = "Arrival")]
    public TimeOnly ArrivalTime { get; set; } = new(10, 0);

    [Display(Name = "Operating days")]
    public List<DayOfWeek> OperatingDays { get; set; } = new();

    [Range(10, 1000, ErrorMessage = "Capacity must be between 10 and 1000 seats.")]
    [Display(Name = "Seats")]
    public int Capacity { get; set; } = 110;

    public List<Route> Routes { get; set; } = new();

    public bool IsEdit => Id > 0;

    public TrainService ToService() => new()
    {
        Id = Id,
        TrainNumber = TrainNumber,
        RouteId = RouteId,
        DepartureTime = DepartureTime,
        ArrivalTime = ArrivalTime,
        OperatingDays = OperatingDays.ToArray(),
        Capacity = Capacity
    };

    public static TrainServiceFormModel FromService(TrainService service) => new()
    {
        Id = service.Id,
        TrainNumber = service.TrainNumber,
        RouteId = service.RouteId,
        DepartureTime = service.DepartureTime,
        ArrivalTime = service.ArrivalTime,
        OperatingDays = service.OperatingDays.ToList(),
        Capacity = service.Capacity
    };
}

/// <summary>Backs the create/edit screens for a special request.</summary>
public class SpecialRequestFormModel
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose the booking this request belongs to.")]
    [Display(Name = "Booking")]
    public int BookingId { get; set; }

    [Display(Name = "Type")]
    public RequestType Type { get; set; }

    [StringLength(250)]
    [Display(Name = "Details")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Status")]
    public RequestStatus Status { get; set; } = RequestStatus.Requested;

    /// <summary>The user's bookings, for the "which booking?" dropdown.</summary>
    public List<Booking> Bookings { get; set; } = new();

    public bool IsEdit => Id > 0;

    public SpecialRequest ToRequest() => new()
    {
        Id = Id,
        BookingId = BookingId,
        Type = Type,
        Description = Description,
        Status = Status
    };

    public static SpecialRequestFormModel FromRequest(SpecialRequest request) => new()
    {
        Id = request.Id,
        BookingId = request.BookingId,
        Type = request.Type,
        Description = request.Description,
        Status = request.Status
    };
}

/// <summary>A special request shown alongside the booking it belongs to.</summary>
public class SpecialRequestListItem
{
    public required SpecialRequest Request { get; init; }
    public Booking? Booking { get; init; }
}

/// <summary>The weekly view and the weekly report both render from this.</summary>
public class WeeklyViewModel
{
    public required WeeklyView Week { get; init; }

    public DateOnly PreviousWeek => Week.WeekStart.AddDays(-7);
    public DateOnly NextWeek => Week.WeekStart.AddDays(7);

    /// <summary>Bookings grouped by the day of the week they fall on, for the report's per-day summary.</summary>
    public IEnumerable<WeeklyDay> DaysWithActivity => Week.Days.Where(d => d.Occurrences.Count > 0);
}

/// <summary>The chat page: the running transcript plus the dropdowns used to disambiguate.</summary>
public class ChatViewModel
{
    public string? Question { get; set; }

    public List<ChatMessage> Transcript { get; set; } = new();

    public List<Route> Routes { get; set; } = new();

    public int? RouteId { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? Date { get; set; }
}

/// <summary>One turn of the conversation. Held in session so the transcript survives a page post.</summary>
public class ChatMessage
{
    public required string Text { get; init; }
    public required bool FromUser { get; init; }
    public Prediction? Prediction { get; init; }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
