using TrainReservation.Domain;

namespace TrainReservation.Data.Repositories;

/// <summary>
/// Base for the dictionary-backed repositories. Subclasses supply the dictionary they own and
/// how to mint an id; everything else (CRUD + locking) is shared.
/// </summary>
public abstract class InMemoryRepository<T> : IRepository<T> where T : class, IEntity
{
    protected readonly InMemoryDataStore Store;

    protected InMemoryRepository(InMemoryDataStore store) => Store = store;

    /// <summary>The dictionary this repository owns.</summary>
    protected abstract Dictionary<int, T> Items { get; }

    protected abstract int NextId();

    public virtual IEnumerable<T> GetAll()
    {
        // Copy inside the lock: callers enumerate lazily, and the store could be mutated meanwhile.
        lock (Store.Sync)
            return Items.Values.ToList();
    }

    public virtual T? GetById(int id)
    {
        lock (Store.Sync)
            return Items.TryGetValue(id, out var item) ? item : null;
    }

    public virtual T Add(T entity)
    {
        lock (Store.Sync)
        {
            entity.Id = NextId();
            Items[entity.Id] = entity;
            return entity;
        }
    }

    public virtual bool Update(T entity)
    {
        lock (Store.Sync)
        {
            if (!Items.ContainsKey(entity.Id)) return false;
            Items[entity.Id] = entity;
            return true;
        }
    }

    public virtual bool Delete(int id)
    {
        lock (Store.Sync)
            return Items.Remove(id);
    }
}

public class InMemoryUserRepository : InMemoryRepository<User>, IUserRepository
{
    public InMemoryUserRepository(InMemoryDataStore store) : base(store) { }

    protected override Dictionary<int, User> Items => Store.Users;
    protected override int NextId() => Store.NextUserId();

    public User GetCurrentUser()
    {
        lock (Store.Sync)
            return Store.Users.Values.First();
    }
}

public class InMemoryStationRepository : IStationRepository
{
    private readonly InMemoryDataStore _store;

    public InMemoryStationRepository(InMemoryDataStore store) => _store = store;

    public IEnumerable<Station> GetAll()
    {
        lock (_store.Sync)
            return _store.Stations.Values.OrderBy(s => s.Name).ToList();
    }

    public Station? GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        lock (_store.Sync)
            return _store.Stations.TryGetValue(code, out var station) ? station : null;
    }

    public Station Add(Station station)
    {
        lock (_store.Sync)
        {
            _store.Stations[station.Code] = station;
            return station;
        }
    }
}

public class InMemoryRouteRepository : InMemoryRepository<Route>, IRouteRepository
{
    public InMemoryRouteRepository(InMemoryDataStore store) : base(store) { }

    protected override Dictionary<int, Route> Items => Store.Routes;
    protected override int NextId() => Store.NextRouteId();

    public Route? FindByStations(string originCode, string destinationCode)
    {
        lock (Store.Sync)
            return Store.Routes.Values.FirstOrDefault(r =>
                string.Equals(r.OriginStation.Code, originCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.DestinationStation.Code, destinationCode, StringComparison.OrdinalIgnoreCase));
    }
}

public class InMemoryTrainServiceRepository : InMemoryRepository<TrainService>, ITrainServiceRepository
{
    public InMemoryTrainServiceRepository(InMemoryDataStore store) : base(store) { }

    protected override Dictionary<int, TrainService> Items => Store.TrainServices;
    protected override int NextId() => Store.NextTrainServiceId();

    public IEnumerable<TrainService> GetByRoute(int routeId)
    {
        lock (Store.Sync)
            return Store.TrainServices.Values
                .Where(s => s.RouteId == routeId)
                .OrderBy(s => s.DepartureTime)
                .ToList();
    }

    public IEnumerable<TrainService> GetOperatingOn(int routeId, DateOnly date)
    {
        lock (Store.Sync)
            return Store.TrainServices.Values
                .Where(s => s.RouteId == routeId && s.OperatingDays.Contains(date.DayOfWeek))
                .OrderBy(s => s.DepartureTime)
                .ToList();
    }
}

/// <summary>
/// Booking is the aggregate root: adding or deleting a booking also registers or removes its
/// owned seats and special requests in the store's id indexes, so the two views of the data
/// can never disagree.
/// </summary>
public class InMemoryBookingRepository : InMemoryRepository<Booking>, IBookingRepository
{
    public InMemoryBookingRepository(InMemoryDataStore store) : base(store) { }

    protected override Dictionary<int, Booking> Items => Store.Bookings;
    protected override int NextId() => Store.NextBookingId();

    public override Booking Add(Booking booking)
    {
        lock (Store.Sync)
        {
            booking.Id = Store.NextBookingId();

            if (string.IsNullOrWhiteSpace(booking.Reference))
                booking.Reference = Store.NextBookingReference(booking.Id);

            if (booking.RecurrencePattern is { Id: 0 } pattern)
                pattern.Id = Store.NextRecurrencePatternId();

            foreach (var seat in booking.Seats)
            {
                seat.Id = Store.NextSeatId();
                seat.BookingId = booking.Id;
                Store.Seats[seat.Id] = seat;
            }

            foreach (var request in booking.SpecialRequests)
            {
                request.Id = Store.NextSpecialRequestId();
                request.BookingId = booking.Id;
                Store.SpecialRequests[request.Id] = request;
            }

            Store.Bookings[booking.Id] = booking;
            return booking;
        }
    }

    public override bool Update(Booking booking)
    {
        lock (Store.Sync)
        {
            if (!Store.Bookings.TryGetValue(booking.Id, out var existing)) return false;

            // Drop the old children out of the indexes before re-registering the new set,
            // otherwise seats removed during an edit would linger and still block their seat number.
            foreach (var seat in existing.Seats) Store.Seats.Remove(seat.Id);
            foreach (var request in existing.SpecialRequests) Store.SpecialRequests.Remove(request.Id);

            if (booking.RecurrencePattern is { Id: 0 } pattern)
                pattern.Id = Store.NextRecurrencePatternId();

            foreach (var seat in booking.Seats)
            {
                if (seat.Id == 0) seat.Id = Store.NextSeatId();
                seat.BookingId = booking.Id;
                Store.Seats[seat.Id] = seat;
            }

            foreach (var request in booking.SpecialRequests)
            {
                if (request.Id == 0) request.Id = Store.NextSpecialRequestId();
                request.BookingId = booking.Id;
                Store.SpecialRequests[request.Id] = request;
            }

            Store.Bookings[booking.Id] = booking;
            return true;
        }
    }

    public override bool Delete(int id)
    {
        lock (Store.Sync)
        {
            if (!Store.Bookings.TryGetValue(id, out var booking)) return false;

            foreach (var seat in booking.Seats) Store.Seats.Remove(seat.Id);
            foreach (var request in booking.SpecialRequests) Store.SpecialRequests.Remove(request.Id);

            // Deleting a recurring series takes its materialised one-off children with it,
            // otherwise they would be orphaned occurrences of a series that no longer exists.
            var children = Store.Bookings.Values.Where(b => b.ParentBookingId == id).Select(b => b.Id).ToList();
            foreach (var childId in children)
            {
                var child = Store.Bookings[childId];
                foreach (var seat in child.Seats) Store.Seats.Remove(seat.Id);
                foreach (var request in child.SpecialRequests) Store.SpecialRequests.Remove(request.Id);
                Store.Bookings.Remove(childId);
            }

            return Store.Bookings.Remove(id);
        }
    }

    public IEnumerable<Booking> GetByTravelDateRange(DateOnly from, DateOnly to)
    {
        lock (Store.Sync)
            return Store.Bookings.Values
                .Where(b => !b.IsRecurring && b.TravelDate >= from && b.TravelDate <= to)
                .OrderBy(b => b.TravelDate)
                .ToList();
    }

    public IEnumerable<Booking> GetRecurring()
    {
        lock (Store.Sync)
            return Store.Bookings.Values
                .Where(b => b is { IsRecurring: true, RecurrencePattern: not null })
                .ToList();
    }

    public IEnumerable<Booking> GetByServiceAndDate(int trainServiceId, DateOnly travelDate)
    {
        lock (Store.Sync)
            return Store.Bookings.Values
                .Where(b => !b.IsRecurring && b.TrainServiceId == trainServiceId && b.TravelDate == travelDate)
                .ToList();
    }

    public Booking? GetMaterialisedOccurrence(int parentBookingId, DateOnly travelDate)
    {
        lock (Store.Sync)
            return Store.Bookings.Values
                .FirstOrDefault(b => b.ParentBookingId == parentBookingId && b.TravelDate == travelDate);
    }
}

/// <summary>
/// Special requests live in the store's id index *and* in their owning booking's collection —
/// the same object reference in both, so an edit through either path is visible from the other.
/// </summary>
public class InMemorySpecialRequestRepository : InMemoryRepository<SpecialRequest>, ISpecialRequestRepository
{
    public InMemorySpecialRequestRepository(InMemoryDataStore store) : base(store) { }

    protected override Dictionary<int, SpecialRequest> Items => Store.SpecialRequests;
    protected override int NextId() => Store.NextSpecialRequestId();

    public IEnumerable<SpecialRequest> GetByBooking(int bookingId)
    {
        lock (Store.Sync)
            return Store.SpecialRequests.Values.Where(r => r.BookingId == bookingId).ToList();
    }

    public SpecialRequest AddToBooking(int bookingId, SpecialRequest request)
    {
        lock (Store.Sync)
        {
            if (!Store.Bookings.TryGetValue(bookingId, out var booking))
                throw new InvalidOperationException($"Booking {bookingId} does not exist.");

            request.Id = Store.NextSpecialRequestId();
            request.BookingId = bookingId;

            Store.SpecialRequests[request.Id] = request;
            booking.SpecialRequests.Add(request);

            return request;
        }
    }

    public override bool Update(SpecialRequest request)
    {
        lock (Store.Sync)
        {
            if (!Store.SpecialRequests.TryGetValue(request.Id, out var existing)) return false;

            // Mutate in place rather than swapping the reference: the owning booking's list holds
            // the original instance, and replacing the dictionary entry alone would desync them.
            existing.Type = request.Type;
            existing.Description = request.Description;
            existing.Status = request.Status;

            return true;
        }
    }

    public override bool Delete(int id)
    {
        lock (Store.Sync)
        {
            if (!Store.SpecialRequests.TryGetValue(id, out var request)) return false;

            if (Store.Bookings.TryGetValue(request.BookingId, out var booking))
                booking.SpecialRequests.RemoveAll(r => r.Id == id);

            return Store.SpecialRequests.Remove(id);
        }
    }
}
