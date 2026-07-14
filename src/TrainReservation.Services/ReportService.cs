using TrainReservation.Data.Repositories;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Services;

/// <summary>
/// Builds the Monday–Sunday window that both the weekly view and the weekly report render.
///
/// The interesting part is recurrence: a series is stored once, so the week has to be assembled by
/// expanding each series across the seven days and dropping any date the user has already split
/// out into a booking of its own.
/// </summary>
public class ReportService : IReportService
{
    private readonly IBookingRepository _bookings;
    private readonly ITrainServiceRepository _services;
    private readonly IRouteRepository _routes;
    private readonly IUserRepository _users;

    public ReportService(
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

    public DateOnly StartOfWeek(DateOnly date) => RecurrenceExpander.StartOfWeek(date);

    public WeeklyView GetWeek(DateOnly anyDateInWeek)
    {
        var me = _users.GetCurrentUser().Id;
        var weekStart = StartOfWeek(anyDateInWeek);
        var weekEnd = weekStart.AddDays(6);

        // Bucket by date so each day's list is built in one pass rather than re-scanning per day.
        var byDate = Enumerable.Range(0, 7)
            .ToDictionary(i => weekStart.AddDays(i), _ => new List<BookingOccurrence>());

        // 1. One-off bookings (including occurrences already materialised out of a series).
        foreach (var booking in _bookings.GetByTravelDateRange(weekStart, weekEnd))
        {
            if (booking.UserId != me) continue;

            byDate[booking.TravelDate].Add(new BookingOccurrence
            {
                Booking = Hydrate(booking),
                Date = booking.TravelDate,
                IsGenerated = false,
                IsMaterialised = booking.ParentBookingId is not null,
                SeriesId = booking.ParentBookingId
            });
        }

        // 2. Recurring series, expanded across this week only.
        foreach (var series in _bookings.GetRecurring())
        {
            if (series.UserId != me) continue;

            var hydrated = Hydrate(series);

            foreach (var date in RecurrenceExpander.Expand(series.RecurrencePattern!, weekStart, weekEnd))
            {
                // Skip dates the user has split out: that occurrence is already in the week as its
                // own booking (step 1), and showing the parent too would double-count it.
                if (_bookings.GetMaterialisedOccurrence(series.Id, date) is not null) continue;

                byDate[date].Add(new BookingOccurrence
                {
                    Booking = hydrated,
                    Date = date,
                    IsGenerated = true,
                    IsMaterialised = false,
                    SeriesId = series.Id
                });
            }
        }

        var days = byDate
            .OrderBy(kv => kv.Key)
            .Select(kv => new WeeklyDay
            {
                Date = kv.Key,
                Occurrences = kv.Value
                    .OrderBy(o => o.Booking.TrainService?.DepartureTime ?? TimeOnly.MinValue)
                    .ToList()
            })
            .ToList();

        return new WeeklyView { WeekStart = weekStart, Days = days };
    }

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
