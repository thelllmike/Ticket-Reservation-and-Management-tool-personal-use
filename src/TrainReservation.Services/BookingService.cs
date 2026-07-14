using TrainReservation.Data.Repositories;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Services;

/// <summary>
/// The booking rules: what may be booked, what a booking costs, and how a recurring series is
/// split apart when the user wants to change a single date.
/// </summary>
public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookings;
    private readonly ITrainServiceRepository _services;
    private readonly IRouteRepository _routes;
    private readonly IUserRepository _users;

    public BookingService(
        IBookingRepository bookings,
        ITrainServiceRepository services,
        IRouteRepository routes,
        IUserRepository users)
    {
        _bookings = bookings;
        _services = services;
        _routes = routes;
        _users = users;
    }

    public IEnumerable<Booking> GetMyBookings()
    {
        var me = _users.GetCurrentUser().Id;

        return _bookings.GetAll()
            .Where(b => b.UserId == me)
            .Select(Hydrate)
            .OrderByDescending(b => b.TravelDate)
            .ThenBy(b => b.TrainService?.DepartureTime);
    }

    public Booking? GetBooking(int id)
    {
        var booking = _bookings.GetById(id);
        return booking is null ? null : Hydrate(booking);
    }

    public OperationResult Create(Booking booking)
    {
        booking.UserId = _users.GetCurrentUser().Id;

        var errors = Validate(booking);
        if (errors.Count > 0) return OperationResult.Fail(errors);

        // Price is always recomputed from the seats, never taken from the form — otherwise a
        // tampered-with field could book a first-class seat at the standard fare.
        booking.TotalPrice = booking.CalculateTotal();
        booking.DateCreated = DateTime.Now;

        if (!booking.IsRecurring)
            booking.RecurrencePattern = null;

        var created = _bookings.Add(booking);
        return OperationResult.Ok(created.Id);
    }

    public OperationResult Update(Booking booking)
    {
        var existing = _bookings.GetById(booking.Id);
        if (existing is null) return OperationResult.Fail("That booking no longer exists.");

        // Preserve the fields the edit form does not own.
        booking.UserId = existing.UserId;
        booking.Reference = existing.Reference;
        booking.DateCreated = existing.DateCreated;
        booking.ParentBookingId = existing.ParentBookingId;

        var errors = Validate(booking, allowPastDate: existing.TravelDate < Today);
        if (errors.Count > 0) return OperationResult.Fail(errors);

        booking.TotalPrice = booking.CalculateTotal();

        if (!booking.IsRecurring)
            booking.RecurrencePattern = null;

        return _bookings.Update(booking)
            ? OperationResult.Ok(booking.Id)
            : OperationResult.Fail("Could not update the booking.");
    }

    public OperationResult Cancel(int id)
    {
        var booking = _bookings.GetById(id);
        if (booking is null) return OperationResult.Fail("That booking no longer exists.");

        if (booking.Status == BookingStatus.Cancelled)
            return OperationResult.Fail("That booking is already cancelled.");

        booking.Status = BookingStatus.Cancelled;

        return _bookings.Update(booking)
            ? OperationResult.Ok(id)
            : OperationResult.Fail("Could not cancel the booking.");
    }

    public OperationResult Delete(int id) =>
        _bookings.Delete(id)
            ? OperationResult.Ok()
            : OperationResult.Fail("That booking no longer exists.");

    public IReadOnlyCollection<string> GetTakenSeats(int trainServiceId, DateOnly travelDate, int? excludingBookingId = null)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Seats held by concrete bookings on this service and date.
        foreach (var booking in _bookings.GetByServiceAndDate(trainServiceId, travelDate))
        {
            if (booking.Status == BookingStatus.Cancelled) continue;
            if (excludingBookingId is not null && booking.Id == excludingBookingId) continue;

            foreach (var seat in booking.Seats) taken.Add(seat.Label);
        }

        // Seats held by recurring series that happen to fall on this date. These occurrences are
        // never stored, so they would otherwise be invisible to the double-booking check.
        foreach (var series in _bookings.GetRecurring())
        {
            if (series.Status == BookingStatus.Cancelled) continue;
            if (series.TrainServiceId != trainServiceId) continue;
            if (excludingBookingId is not null && series.Id == excludingBookingId) continue;

            var hitsThisDate = RecurrenceExpander
                .Expand(series.RecurrencePattern!, travelDate, travelDate)
                .Any();

            if (!hitsThisDate) continue;

            // If the user already split this date out of the series, the materialised child holds
            // the seats and was counted above — counting the parent too would flag a phantom clash.
            if (_bookings.GetMaterialisedOccurrence(series.Id, travelDate) is not null) continue;

            foreach (var seat in series.Seats) taken.Add(seat.Label);
        }

        return taken;
    }

    public decimal GetSeatPrice(int routeId, TravelClass travelClass)
    {
        var route = _routes.GetById(routeId);
        return route is null ? 0m : PricingCalculator.SeatPrice(route.DistanceKm, travelClass);
    }

    public OperationResult MaterialiseOccurrence(int seriesId, DateOnly date)
    {
        var series = _bookings.GetById(seriesId);
        if (series is null || !series.IsRecurring || series.RecurrencePattern is null)
            return OperationResult.Fail("That recurring booking no longer exists.");

        // Already split out — hand back the existing child rather than creating a duplicate.
        var existing = _bookings.GetMaterialisedOccurrence(seriesId, date);
        if (existing is not null) return OperationResult.Ok(existing.Id);

        if (!RecurrenceExpander.Expand(series.RecurrencePattern, date, date).Any())
            return OperationResult.Fail("That series does not run on the chosen date.");

        // Copy the series onto the single date. Seats and requests are cloned, not shared, so
        // editing the occurrence cannot reach back and mutate the series.
        var occurrence = new Booking
        {
            UserId = series.UserId,
            TrainServiceId = series.TrainServiceId,
            TravelDate = date,
            DateCreated = DateTime.Now,
            Status = series.Status,
            IsRecurring = false,
            ParentBookingId = seriesId,
            Seats = series.Seats.Select(s => new Seat
            {
                Coach = s.Coach,
                SeatNumber = s.SeatNumber,
                TravelClass = s.TravelClass,
                Price = s.Price
            }).ToList(),
            SpecialRequests = series.SpecialRequests.Select(r => new SpecialRequest
            {
                Type = r.Type,
                Description = r.Description,
                Status = r.Status
            }).ToList()
        };

        occurrence.TotalPrice = occurrence.CalculateTotal();

        var created = _bookings.Add(occurrence);
        return OperationResult.Ok(created.Id);
    }

    // -----------------------------------------------------------------------------------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// Server-side rules. These run regardless of what the browser did, so the data annotations on
    /// the model are a convenience for the user rather than the actual guarantee.
    /// </summary>
    private List<string> Validate(Booking booking, bool allowPastDate = false)
    {
        var errors = new List<string>();

        var service = _services.GetById(booking.TrainServiceId);
        if (service is null)
        {
            errors.Add("Choose a train service.");
            return errors; // nothing further can be checked without a service
        }

        if (_routes.GetById(service.RouteId) is null)
            errors.Add("That service is not attached to a valid route.");

        // A booking may be edited after the fact (to mark it completed, say), so an existing past
        // date is tolerated — but a new one can never be created in the past.
        if (!allowPastDate && booking.TravelDate < Today)
            errors.Add("Travel date cannot be in the past.");

        if (!service.RunsOn(booking.TravelDate))
            errors.Add($"Train {service.TrainNumber} does not run on a {booking.TravelDate.DayOfWeek}.");

        if (booking.Seats.Count == 0)
            errors.Add("Select at least one seat.");

        if (booking.Seats.Count > service.Capacity)
            errors.Add($"Train {service.TrainNumber} only has {service.Capacity} seats.");

        // Two seats with the same label inside the one booking.
        var duplicates = booking.Seats
            .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            errors.Add($"Seat {string.Join(", ", duplicates)} is listed twice on this booking.");

        // The same seat already sold to someone else on this service and date.
        var taken = GetTakenSeats(booking.TrainServiceId, booking.TravelDate, excludingBookingId: booking.Id);
        var clashes = booking.Seats
            .Where(s => taken.Contains(s.Label))
            .Select(s => s.Label)
            .Distinct()
            .ToList();

        if (clashes.Count > 0)
            errors.Add($"Seat {string.Join(", ", clashes)} is already booked on this service for {booking.TravelDate:d MMM yyyy}.");

        if (booking.IsRecurring)
            errors.AddRange(ValidateRecurrence(booking));

        return errors;
    }

    private static List<string> ValidateRecurrence(Booking booking)
    {
        var errors = new List<string>();
        var pattern = booking.RecurrencePattern;

        if (pattern is null)
        {
            errors.Add("A recurring booking needs a repeat pattern.");
            return errors;
        }

        if (pattern.Interval < 1)
            errors.Add("The repeat interval must be at least 1.");

        if (pattern.EndDate < pattern.StartDate)
            errors.Add("The pattern's end date must be on or after its start date.");

        if (pattern.Frequency == Frequency.Weekly && pattern.DaysOfWeek.Length == 0)
            errors.Add("Choose at least one day of the week for a weekly repeat.");

        return errors;
    }

    /// <summary>Populates the navigation properties the store does not maintain.</summary>
    private Booking Hydrate(Booking booking)
    {
        var service = _services.GetById(booking.TrainServiceId);

        if (service is not null)
        {
            service.Route = _routes.GetById(service.RouteId);
            booking.TrainService = service;
        }

        return booking;
    }
}
