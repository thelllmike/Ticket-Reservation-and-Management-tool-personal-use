using TrainReservation.Domain;

namespace TrainReservation.Data.Repositories;

/// <summary>
/// Storage contract shared by every id-keyed entity. The layers above depend only on this
/// abstraction, so the in-memory implementation can later be replaced by a database-backed
/// one without any change to the services, controllers or views.
/// </summary>
public interface IRepository<T> where T : class, IEntity
{
    IEnumerable<T> GetAll();
    T? GetById(int id);
    T Add(T entity);
    bool Update(T entity);
    bool Delete(int id);
}

public interface IUserRepository : IRepository<User>
{
    /// <summary>The single seeded personal user (no auth in this iteration).</summary>
    User GetCurrentUser();
}

/// <summary>Stations are keyed by code, so this repository does not extend <see cref="IRepository{T}"/>.</summary>
public interface IStationRepository
{
    IEnumerable<Station> GetAll();
    Station? GetByCode(string code);
    Station Add(Station station);
}

public interface IRouteRepository : IRepository<Route>
{
    /// <summary>Used by the chatbot to resolve "London to Manchester" onto a route.</summary>
    Route? FindByStations(string originCode, string destinationCode);
}

public interface ITrainServiceRepository : IRepository<TrainService>
{
    IEnumerable<TrainService> GetByRoute(int routeId);

    /// <summary>Services on a route that actually run on the given date's day of week.</summary>
    IEnumerable<TrainService> GetOperatingOn(int routeId, DateOnly date);
}

public interface IBookingRepository : IRepository<Booking>
{
    /// <summary>One-off bookings whose travel date falls in the range. Excludes recurring series.</summary>
    IEnumerable<Booking> GetByTravelDateRange(DateOnly from, DateOnly to);

    /// <summary>Every recurring series, so callers can expand its occurrences on demand.</summary>
    IEnumerable<Booking> GetRecurring();

    /// <summary>Bookings on a given service and date, used for double-booking and occupancy checks.</summary>
    IEnumerable<Booking> GetByServiceAndDate(int trainServiceId, DateOnly travelDate);

    /// <summary>The materialised child of a recurring series for one specific date, if the user has edited it.</summary>
    Booking? GetMaterialisedOccurrence(int parentBookingId, DateOnly travelDate);
}

public interface ISpecialRequestRepository : IRepository<SpecialRequest>
{
    IEnumerable<SpecialRequest> GetByBooking(int bookingId);

    /// <summary>Adds the request to the store's index and to its owning booking in one step.</summary>
    SpecialRequest AddToBooking(int bookingId, SpecialRequest request);
}
