using Microsoft.AspNetCore.Mvc;
using TrainReservation.Services;
using TrainReservation.Web.Models;

namespace TrainReservation.Web.Controllers;

/// <summary>CRUD for the schedule — the train services that run on each route.</summary>
public class ScheduleController : Controller
{
    private readonly IScheduleService _schedule;

    public ScheduleController(IScheduleService schedule) => _schedule = schedule;

    public IActionResult Index() => View(_schedule.GetServices().ToList());

    public IActionResult Details(int id)
    {
        var service = _schedule.GetService(id);
        return service is null ? NotFound() : View(service);
    }

    public IActionResult Create()
    {
        var model = new TrainServiceFormModel { Routes = _schedule.GetRoutes().ToList() };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(TrainServiceFormModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Routes = _schedule.GetRoutes().ToList();
            return View(model);
        }

        var result = _schedule.CreateService(model.ToService());

        if (!result.Success)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            model.Routes = _schedule.GetRoutes().ToList();
            return View(model);
        }

        TempData["Success"] = $"Train {model.TrainNumber} added to the schedule.";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Edit(int id)
    {
        var service = _schedule.GetService(id);
        if (service is null) return NotFound();

        var model = TrainServiceFormModel.FromService(service);
        model.Routes = _schedule.GetRoutes().ToList();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(TrainServiceFormModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Routes = _schedule.GetRoutes().ToList();
            return View(model);
        }

        var result = _schedule.UpdateService(model.ToService());

        if (!result.Success)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            model.Routes = _schedule.GetRoutes().ToList();
            return View(model);
        }

        TempData["Success"] = $"Train {model.TrainNumber} updated.";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Delete(int id)
    {
        var service = _schedule.GetService(id);
        return service is null ? NotFound() : View(service);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteConfirmed(int id)
    {
        var result = _schedule.DeleteService(id);

        if (!result.Success)
        {
            // Most likely the service still has bookings against it — show why rather than a bare failure.
            TempData["Error"] = string.Join(" ", result.Errors);
            return RedirectToAction(nameof(Delete), new { id });
        }

        TempData["Success"] = "Service removed from the schedule.";
        return RedirectToAction(nameof(Index));
    }
}
