using TrainReservation.Data.Repositories;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Services;

/// <summary>
/// The schedule: stations, routes, and CRUD over the train services that run on them.
/// </summary>
public class ScheduleService : IScheduleService
{
    private readonly IStationRepository _stations;
    private readonly IRouteRepository _routes;
    private readonly ITrainServiceRepository _services;
    private readonly IBookingRepository _bookings;

    public ScheduleService(
        IStationRepository stations,
        IRouteRepository routes,
        ITrainServiceRepository services,
        IBookingRepository bookings)
    {
        _stations = stations;
        _routes = routes;
        _services = services;
        _bookings = bookings;
    }

    public IEnumerable<Station> GetStations() => _stations.GetAll();

    public IEnumerable<Route> GetRoutes() => _routes.GetAll().OrderBy(r => r.Description);

    public Route? GetRoute(int routeId) => _routes.GetById(routeId);

    public IEnumerable<TrainService> GetServices() => _services.GetAll()
        .Select(WithRoute)
        .OrderBy(s => s.Route?.Description)
        .ThenBy(s => s.DepartureTime);

    public TrainService? GetService(int id)
    {
        var service = _services.GetById(id);
        return service is null ? null : WithRoute(service);
    }

    public IEnumerable<TrainService> GetAvailableServices(int routeId, DateOnly travelDate) =>
        _services.GetOperatingOn(routeId, travelDate)
            .Select(WithRoute)
            .OrderBy(s => s.DepartureTime);

    public OperationResult CreateService(TrainService service)
    {
        var errors = Validate(service);
        if (errors.Count > 0) return OperationResult.Fail(errors);

        var created = _services.Add(service);
        return OperationResult.Ok(created.Id);
    }

    public OperationResult UpdateService(TrainService service)
    {
        if (_services.GetById(service.Id) is null)
            return OperationResult.Fail("That service no longer exists.");

        var errors = Validate(service);
        if (errors.Count > 0) return OperationResult.Fail(errors);

        return _services.Update(service)
            ? OperationResult.Ok(service.Id)
            : OperationResult.Fail("Could not update the service.");
    }

    public OperationResult DeleteService(int id)
    {
        // Refuse to strand bookings: a service with reservations against it cannot simply vanish.
        var booked = _bookings.GetAll().Any(b => b.TrainServiceId == id && b.Status != BookingStatus.Cancelled);
        if (booked)
            return OperationResult.Fail("This service has bookings against it. Cancel those bookings before deleting it.");

        return _services.Delete(id)
            ? OperationResult.Ok()
            : OperationResult.Fail("That service no longer exists.");
    }

    /// <summary>Server-side rules that data annotations alone cannot express.</summary>
    private List<string> Validate(TrainService service)
    {
        var errors = new List<string>();

        if (_routes.GetById(service.RouteId) is null)
            errors.Add("Choose a route for this service.");

        if (service.OperatingDays.Length == 0)
            errors.Add("A service must run on at least one day of the week.");

        if (service.ArrivalTime <= service.DepartureTime)
            errors.Add("Arrival time must be after the departure time.");

        if (service.Capacity < 10)
            errors.Add("Capacity must be at least 10 seats.");

        return errors;
    }

    /// <summary>Populates the Route navigation property, which the store does not do for us.</summary>
    private TrainService WithRoute(TrainService service)
    {
        service.Route = _routes.GetById(service.RouteId);
        return service;
    }
}
