using Microsoft.AspNetCore.Mvc;
using TrainReservation.Domain;
using TrainReservation.Services;
using TrainReservation.Web.Models;

namespace TrainReservation.Web.Controllers;

/// <summary>
/// CRUD for bookings, plus the guided add-booking flow
/// (route → date → service → seats → special requests → confirm).
/// </summary>
public class BookingsController : Controller
{
    private readonly IBookingService _bookings;
    private readonly IScheduleService _schedule;

    public BookingsController(IBookingService bookings, IScheduleService schedule)
    {
        _bookings = bookings;
        _schedule = schedule;
    }

    // ---- Read --------------------------------------------------------------------------------

    public IActionResult Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var all = _bookings.GetMyBookings().ToList();

        var model = new BookingListViewModel
        {
            // A recurring series has no single travel date, so it gets its own section rather than
            // being filed under whichever date it happened to be created with.
            Recurring = all.Where(b => b.IsRecurring).ToList(),
            Upcoming = all.Where(b => !b.IsRecurring && b.TravelDate >= today)
                          .OrderBy(b => b.TravelDate)
                          .ToList(),
            Past = all.Where(b => !b.IsRecurring && b.TravelDate < today).ToList()
        };

        return View(model);
    }

    public IActionResult Details(int id)
    {
        var booking = _bookings.GetBooking(id);
        return booking is null ? NotFound() : View(booking);
    }

    // ---- Create ------------------------------------------------------------------------------

    public IActionResult Create()
    {
        var model = new BookingFormModel();
        Populate(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(BookingFormModel model)
    {
        var route = _schedule.GetRoute(model.RouteId);

        if (route is null)
            ModelState.AddModelError(nameof(model.RouteId), "Choose a route.");

        if (!ModelState.IsValid || route is null)
        {
            Populate(model);
            return View(model);
        }

        var result = _bookings.Create(model.ToBooking(route.DistanceKm));

        if (!result.Success)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            Populate(model);
            return View(model);
        }

        TempData["Success"] = "Booking created.";
        return RedirectToAction(nameof(Details), new { id = result.EntityId });
    }

    // ---- Update ------------------------------------------------------------------------------

    public IActionResult Edit(int id)
    {
        var booking = _bookings.GetBooking(id);
        if (booking is null) return NotFound();

        var model = BookingFormModel.FromBooking(booking);
        Populate(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(BookingFormModel model)
    {
        var route = _schedule.GetRoute(model.RouteId);

        if (route is null)
            ModelState.AddModelError(nameof(model.RouteId), "Choose a route.");

        if (!ModelState.IsValid || route is null)
        {
            Populate(model);
            return View(model);
        }

        var result = _bookings.Update(model.ToBooking(route.DistanceKm));

        if (!result.Success)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            Populate(model);
            return View(model);
        }

        TempData["Success"] = "Booking updated.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // ---- Cancel / Delete ---------------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(int id)
    {
        var result = _bookings.Cancel(id);

        TempData[result.Success ? "Success" : "Error"] =
            result.Success ? "Booking cancelled." : string.Join(" ", result.Errors);

        return RedirectToAction(nameof(Details), new { id });
    }

    public IActionResult Delete(int id)
    {
        var booking = _bookings.GetBooking(id);
        return booking is null ? NotFound() : View(booking);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteConfirmed(int id)
    {
        var result = _bookings.Delete(id);

        TempData[result.Success ? "Success" : "Error"] =
            result.Success ? "Booking deleted." : string.Join(" ", result.Errors);

        return RedirectToAction(nameof(Index));
    }

    // ---- Recurring occurrences ----------------------------------------------------------------

    /// <summary>
    /// Splits a single date out of a recurring series so it can be changed on its own. Used by the
    /// "edit just this one" and "cancel just this one" actions in the weekly view.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult MaterialiseOccurrence(int seriesId, DateOnly date, string action = "edit")
    {
        var result = _bookings.MaterialiseOccurrence(seriesId, date);

        if (!result.Success)
        {
            TempData["Error"] = string.Join(" ", result.Errors);
            return RedirectToAction("Index", "Weekly", new { week = date.ToString("yyyy-MM-dd") });
        }

        var occurrenceId = result.EntityId!.Value;

        if (action == "cancel")
        {
            _bookings.Cancel(occurrenceId);
            TempData["Success"] = $"Cancelled the {date:d MMM} occurrence only. The rest of the series is unchanged.";
            return RedirectToAction("Index", "Weekly", new { week = date.ToString("yyyy-MM-dd") });
        }

        TempData["Success"] = $"The {date:d MMM} occurrence is now a booking of its own. Editing it will not affect the series.";
        return RedirectToAction(nameof(Edit), new { id = occurrenceId });
    }

    // ---- JSON endpoints used by the booking form ----------------------------------------------
    // Local only — the app makes no external calls.

    /// <summary>Services running on a route on a given date, for the "available service" step.</summary>
    [HttpGet]
    public IActionResult ServicesFor(int routeId, DateOnly date)
    {
        var services = _schedule.GetAvailableServices(routeId, date)
            .Select(s => new
            {
                id = s.Id,
                label = $"{s.DepartureTime:HH\\:mm} → {s.ArrivalTime:HH\\:mm}  ({s.TrainNumber})",
                departure = s.DepartureTime.ToString("HH\\:mm"),
                capacity = s.Capacity
            });

        return Json(services);
    }

    /// <summary>The seat grid for one service on one date: which seats exist and which are gone.</summary>
    [HttpGet]
    public IActionResult SeatMapFor(int serviceId, DateOnly date, int? excludeBookingId = null)
    {
        var service = _schedule.GetService(serviceId);
        if (service?.Route is null) return NotFound();

        var taken = _bookings.GetTakenSeats(serviceId, date, excludeBookingId);

        var coaches = SeatMap.Coaches(service.Capacity).Select(c => new
        {
            letter = c.Letter,
            travelClass = c.TravelClass.ToString(),
            seats = Enumerable.Range(1, c.SeatCount).Select(n => new
            {
                number = n,
                label = SeatMap.Label(c.Letter, n),
                taken = taken.Contains(SeatMap.Label(c.Letter, n))
            })
        });

        return Json(new
        {
            capacity = service.Capacity,
            standardPrice = _bookings.GetSeatPrice(service.Route.Id, TravelClass.Standard),
            firstClassPrice = _bookings.GetSeatPrice(service.Route.Id, TravelClass.FirstClass),
            coaches
        });
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Refills the parts of the form the user did not type: the dropdown contents, the seat map and
    /// the fares. Called on first render and again whenever validation sends the form back.
    /// </summary>
    private void Populate(BookingFormModel model)
    {
        model.Routes = _schedule.GetRoutes().ToList();

        if (model.RouteId > 0)
        {
            model.Services = _schedule.GetAvailableServices(model.RouteId, model.TravelDate).ToList();

            var route = _schedule.GetRoute(model.RouteId);
            if (route is not null)
            {
                model.StandardPrice = PricingCalculator.SeatPrice(route.DistanceKm, TravelClass.Standard);
                model.FirstClassPrice = PricingCalculator.SeatPrice(route.DistanceKm, TravelClass.FirstClass);
            }
        }

        if (model.TrainServiceId > 0)
        {
            var service = _schedule.GetService(model.TrainServiceId);

            if (service is not null)
            {
                model.Capacity = service.Capacity;
                model.TakenSeats = _bookings
                    .GetTakenSeats(model.TrainServiceId, model.TravelDate, model.IsEdit ? model.Id : null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
