using Microsoft.AspNetCore.Mvc;
using TrainReservation.Services;
using TrainReservation.Web.Models;

namespace TrainReservation.Web.Controllers;

/// <summary>CRUD for the assistance requests attached to bookings.</summary>
public class SpecialRequestsController : Controller
{
    private readonly ISpecialRequestService _requests;
    private readonly IBookingService _bookings;

    public SpecialRequestsController(ISpecialRequestService requests, IBookingService bookings)
    {
        _requests = requests;
        _bookings = bookings;
    }

    public IActionResult Index()
    {
        // Each request is shown with the trip it belongs to — a request without its booking is
        // meaningless to the user.
        var model = _requests.GetAll()
            .Select(r => new SpecialRequestListItem
            {
                Request = r,
                Booking = _bookings.GetBooking(r.BookingId)
            })
            .ToList();

        return View(model);
    }

    public IActionResult Create(int? bookingId)
    {
        var model = new SpecialRequestFormModel
        {
            BookingId = bookingId ?? 0,
            Bookings = BookableBookings()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(SpecialRequestFormModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Bookings = BookableBookings();
            return View(model);
        }

        var result = _requests.Create(model.BookingId, model.ToRequest());

        if (!result.Success)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            model.Bookings = BookableBookings();
            return View(model);
        }

        TempData["Success"] = "Special request added.";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Edit(int id)
    {
        var request = _requests.Get(id);
        if (request is null) return NotFound();

        var model = SpecialRequestFormModel.FromRequest(request);
        model.Bookings = BookableBookings();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(SpecialRequestFormModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Bookings = BookableBookings();
            return View(model);
        }

        var result = _requests.Update(model.ToRequest());

        if (!result.Success)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            model.Bookings = BookableBookings();
            return View(model);
        }

        TempData["Success"] = "Special request updated.";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Delete(int id)
    {
        var request = _requests.Get(id);
        if (request is null) return NotFound();

        var model = new SpecialRequestListItem
        {
            Request = request,
            Booking = _requests.GetOwningBooking(id)
        };

        return View(model);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteConfirmed(int id)
    {
        var result = _requests.Delete(id);

        TempData[result.Success ? "Success" : "Error"] =
            result.Success ? "Special request deleted." : string.Join(" ", result.Errors);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Requests can only hang off bookings that have not been cancelled.</summary>
    private List<Domain.Booking> BookableBookings() =>
        _bookings.GetMyBookings()
            .Where(b => b.Status != Domain.BookingStatus.Cancelled)
            .ToList();
}
