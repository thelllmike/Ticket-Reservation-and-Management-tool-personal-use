using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TrainReservation.Domain;
using TrainReservation.Services;
using TrainReservation.Services.Models;
using TrainReservation.Web.Models;

namespace TrainReservation.Web.Controllers;

/// <summary>The dashboard: what is coming up, and the way in to everything else.</summary>
public class HomeController : Controller
{
    private readonly IBookingService _bookings;
    private readonly IScheduleService _schedule;
    private readonly IReportService _reports;
    private readonly ISpecialRequestService _requests;

    public HomeController(
        IBookingService bookings,
        IScheduleService schedule,
        IReportService reports,
        ISpecialRequestService requests)
    {
        _bookings = bookings;
        _schedule = schedule;
        _reports = reports;
        _requests = requests;
    }

    public IActionResult Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var model = new DashboardViewModel
        {
            Upcoming = _bookings.GetMyBookings()
                .Where(b => !b.IsRecurring && b.TravelDate >= today && b.Status != BookingStatus.Cancelled)
                .OrderBy(b => b.TravelDate)
                .Take(5)
                .ToList(),
            Week = _reports.GetWeek(today),
            ServiceCount = _schedule.GetServices().Count(),
            OpenRequestCount = _requests.GetAll().Count(r => r.Status == RequestStatus.Requested),
            RecurringCount = _bookings.GetMyBookings().Count(b => b.IsRecurring)
        };

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

/// <summary>Everything the dashboard shows.</summary>
public class DashboardViewModel
{
    public List<Booking> Upcoming { get; init; } = new();
    public required WeeklyView Week { get; init; }
    public int ServiceCount { get; init; }
    public int OpenRequestCount { get; init; }
    public int RecurringCount { get; init; }
}
