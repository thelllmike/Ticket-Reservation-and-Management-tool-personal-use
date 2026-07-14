using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Services;

/// <summary>Read access to the reference data the booking flow needs (stations, routes).</summary>
public interface IScheduleService
{
    IEnumerable<Station> GetStations();
    IEnumerable<Route> GetRoutes();
    Route? GetRoute(int routeId);

    /// <summary>All scheduled services, with their Route navigation property populated.</summary>
    IEnumerable<TrainService> GetServices();

    TrainService? GetService(int id);

    /// <summary>Services on a route that actually run on that date — the step-2 list in the booking flow.</summary>
    IEnumerable<TrainService> GetAvailableServices(int routeId, DateOnly travelDate);

    OperationResult CreateService(TrainService service);
    OperationResult UpdateService(TrainService service);
    OperationResult DeleteService(int id);
}

public interface IBookingService
{
    /// <summary>The personal user's bookings, most recent travel date first. Excludes network demand data.</summary>
    IEnumerable<Booking> GetMyBookings();

    Booking? GetBooking(int id);

    OperationResult Create(Booking booking);
    OperationResult Update(Booking booking);
    OperationResult Cancel(int id);
    OperationResult Delete(int id);

    /// <summary>Seat labels already taken on a service and date, so the seat picker can grey them out.</summary>
    IReadOnlyCollection<string> GetTakenSeats(int trainServiceId, DateOnly travelDate, int? excludingBookingId = null);

    /// <summary>Price for one seat on a route, used to compute the running total in the booking flow.</summary>
    decimal GetSeatPrice(int routeId, TravelClass travelClass);

    /// <summary>
    /// Splits one date out of a recurring series into a Booking of its own, so it can be edited or
    /// cancelled without touching the rest of the series. Returns the new booking's id.
    /// </summary>
    OperationResult MaterialiseOccurrence(int seriesId, DateOnly date);
}

public interface ISpecialRequestService
{
    /// <summary>Every request attached to one of the personal user's bookings.</summary>
    IEnumerable<SpecialRequest> GetAll();

    SpecialRequest? Get(int id);
    Booking? GetOwningBooking(int requestId);

    OperationResult Create(int bookingId, SpecialRequest request);
    OperationResult Update(SpecialRequest request);
    OperationResult Delete(int id);
}

public interface IReportService
{
    /// <summary>
    /// The Monday–Sunday window containing <paramref name="anyDateInWeek"/>, with recurring
    /// bookings expanded into their occurrences.
    /// </summary>
    WeeklyView GetWeek(DateOnly anyDateInWeek);

    /// <summary>Monday of the week containing the given date.</summary>
    DateOnly StartOfWeek(DateOnly date);
}

public interface IPredictionService
{
    /// <summary>Answers a free-text question about pricing or availability.</summary>
    ChatResponse Ask(string question);

    /// <summary>
    /// Predicts directly from a chosen route and date, bypassing the text parsing — used by the
    /// dropdowns on the chat page when a question was ambiguous.
    /// </summary>
    ChatResponse Predict(int routeId, DateOnly date, int? trainServiceId = null);
}
