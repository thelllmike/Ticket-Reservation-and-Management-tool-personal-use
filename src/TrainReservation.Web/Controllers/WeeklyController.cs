using Microsoft.AspNetCore.Mvc;
using TrainReservation.Services;
using TrainReservation.Web.Models;

namespace TrainReservation.Web.Controllers;

/// <summary>
/// The weekly view (a Monday–Sunday grid) and the printer-friendly weekly report. Both render the
/// same <see cref="Services.Models.WeeklyView"/>, with recurring bookings already expanded.
/// </summary>
public class WeeklyController : Controller
{
    private readonly IReportService _reports;

    public WeeklyController(IReportService reports) => _reports = reports;

    /// <summary>The Monday–Sunday grid. <paramref name="week"/> is any date inside the week to show.</summary>
    public IActionResult Index(DateOnly? week) => View(BuildModel(week));

    /// <summary>The same week, summarised per day for printing.</summary>
    public IActionResult Report(DateOnly? week) => View(BuildModel(week));

    private WeeklyViewModel BuildModel(DateOnly? week)
    {
        var anchor = week ?? DateOnly.FromDateTime(DateTime.Today);
        return new WeeklyViewModel { Week = _reports.GetWeek(anchor) };
    }
}
