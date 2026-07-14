using TrainReservation.Data.Repositories;
using TrainReservation.Domain;

namespace TrainReservation.Data;

/// <summary>
/// Populates the in-memory store at startup.
///
/// Two distinct bodies of data are seeded, and the difference matters:
///
///  1. The <b>personal user</b> (Id 1) owns a modest set of bookings — some in the past, some in
///     the current and coming weeks, plus recurring series. These are what the Bookings pages,
///     the weekly view and the weekly report show, and what the user edits.
///
///  2. A <b>network booking history</b> owned by other seeded passengers. Availability is
///     predicted from occupancy (seats sold / capacity), which is only meaningful if the history
///     represents everyone travelling on a train rather than one person's own trips. The chatbot
///     analyses this whole body; the CRUD screens deliberately ignore it, so the user's own
///     booking list stays readable.
///
/// The generator uses a fixed random seed, so the same history — and therefore the same
/// predictions — is produced on every run.
/// </summary>
public class DataSeeder
{
    private const int HistoryWeeks = 12;      // how far back the network history runs
    private const int RandomSeed = 20260714;  // fixed => deterministic predictions

    private readonly InMemoryDataStore _store;
    private readonly IStationRepository _stations;
    private readonly IRouteRepository _routes;
    private readonly ITrainServiceRepository _services;
    private readonly IBookingRepository _bookings;
    private readonly IUserRepository _users;

    private readonly Random _rng = new(RandomSeed);

    public DataSeeder(
        InMemoryDataStore store,
        IStationRepository stations,
        IRouteRepository routes,
        ITrainServiceRepository services,
        IBookingRepository bookings,
        IUserRepository users)
    {
        _store = store;
        _stations = stations;
        _routes = routes;
        _services = services;
        _bookings = bookings;
        _users = users;
    }

    /// <summary>Baseline share of seats sold, by day of week. Fridays and Mondays are the commuter peaks.</summary>
    private static readonly Dictionary<DayOfWeek, double> DayDemand = new()
    {
        [DayOfWeek.Monday] = 0.82,
        [DayOfWeek.Tuesday] = 0.62,
        [DayOfWeek.Wednesday] = 0.60,
        [DayOfWeek.Thursday] = 0.72,
        [DayOfWeek.Friday] = 0.93,
        [DayOfWeek.Saturday] = 0.55,
        [DayOfWeek.Sunday] = 0.66
    };

    public void Seed()
    {
        if (_store.Bookings.Count > 0) return; // already seeded

        var personalUser = SeedUsers();
        SeedStations();
        var routes = SeedRoutes();
        var services = SeedTrainServices(routes);

        SeedNetworkHistory(routes, services);
        SeedPersonalBookings(personalUser, routes, services);
    }

    // ---------------------------------------------------------------------------------------
    // Reference data
    // ---------------------------------------------------------------------------------------

    private User SeedUsers()
    {
        var personal = _users.Add(new User { Name = "Alex Carter", Email = "alex.carter@example.com" });

        // Other passengers exist only to own the network booking history (see class summary).
        foreach (var name in new[] { "Network Demand A", "Network Demand B", "Network Demand C" })
            _users.Add(new User { Name = name, Email = $"{name.Replace(' ', '.').ToLowerInvariant()}@example.com" });

        return personal;
    }

    private void SeedStations()
    {
        var stations = new[]
        {
            new Station { Code = "KGX", Name = "London King's Cross", City = "London" },
            new Station { Code = "MAN", Name = "Manchester Piccadilly", City = "Manchester" },
            new Station { Code = "LDS", Name = "Leeds", City = "Leeds" },
            new Station { Code = "EDB", Name = "Edinburgh Waverley", City = "Edinburgh" },
            new Station { Code = "BHM", Name = "Birmingham New Street", City = "Birmingham" },
            new Station { Code = "YRK", Name = "York", City = "York" },
            new Station { Code = "LIV", Name = "Liverpool Lime Street", City = "Liverpool" },
            new Station { Code = "BRI", Name = "Bristol Temple Meads", City = "Bristol" }
        };

        foreach (var station in stations) _stations.Add(station);
    }

    private List<Route> SeedRoutes()
    {
        // Both directions for the busy corridors, one way for the rest.
        var pairs = new (string From, string To, int Km)[]
        {
            ("KGX", "MAN", 296), ("MAN", "KGX", 296),
            ("KGX", "LDS", 280), ("LDS", "KGX", 280),
            ("KGX", "EDB", 632), ("EDB", "KGX", 632),
            ("KGX", "BHM", 190), ("BHM", "KGX", 190),
            ("KGX", "YRK", 303),
            ("KGX", "BRI", 172),
            ("MAN", "LIV", 56),
            ("LDS", "MAN", 70)
        };

        return pairs.Select(p => _routes.Add(new Route
        {
            OriginStation = _stations.GetByCode(p.From)!,
            DestinationStation = _stations.GetByCode(p.To)!,
            DistanceKm = p.Km
        })).ToList();
    }

    private List<TrainService> SeedTrainServices(List<Route> routes)
    {
        var services = new List<TrainService>();
        var trainNumber = 1000;

        // Every route gets an 08:00 departure, so questions like "the 08:00 to Leeds" always resolve.
        var departures = new[]
        {
            (Time: new TimeOnly(6, 45), WeekdaysOnly: true),
            (Time: new TimeOnly(8, 0), WeekdaysOnly: false),
            (Time: new TimeOnly(12, 30), WeekdaysOnly: false),
            (Time: new TimeOnly(17, 15), WeekdaysOnly: false)
        };

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var everyDay = Enum.GetValues<DayOfWeek>();

        foreach (var route in routes)
        {
            // Journey time from distance at a ~130 km/h average, rounded to the next 5 minutes.
            var minutes = (int)Math.Round(route.DistanceKm / 130.0 * 60 / 5) * 5;

            foreach (var (time, weekdaysOnly) in departures)
            {
                services.Add(_services.Add(new TrainService
                {
                    TrainNumber = $"GR{++trainNumber}",
                    RouteId = route.Id,
                    DepartureTime = time,
                    ArrivalTime = time.AddMinutes(minutes),
                    OperatingDays = weekdaysOnly ? weekdays : everyDay,
                    // Long-distance trains run longer formations than the regional shuttles.
                    Capacity = route.DistanceKm > 250 ? 110 : 80
                }));
            }
        }

        return services;
    }

    // ---------------------------------------------------------------------------------------
    // Network history — the evidence the prediction chatbot reasons over
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Generates ~12 weeks of completed bookings on the six busiest routes. Each service-date is
    /// filled to a target occupancy driven by day of week, route popularity and a per-route price
    /// trend, so the chatbot has genuine patterns to find rather than uniform noise.
    /// </summary>
    private void SeedNetworkHistory(List<Route> routes, List<TrainService> services)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var demandUsers = _store.Users.Values.Where(u => u.Id != 1).Select(u => u.Id).ToList();

        // The corridors that carry a history. Each gets a popularity level and a price trend, so the
        // chatbot can legitimately report "rising" on one route and "easing" on another.
        var profiles = new (string From, string To, double Popularity, double WeeklyPriceDrift)[]
        {
            ("KGX", "MAN", 1.00, +0.006),  // busy, fares creeping up
            ("KGX", "LDS", 0.95, +0.008),  // busy, rising fastest
            ("KGX", "EDB", 0.90, +0.004),
            ("KGX", "BHM", 0.85, -0.005),  // softening — fares easing
            ("MAN", "KGX", 0.98, +0.005),
            ("LDS", "KGX", 0.92, +0.003)
        };

        foreach (var (from, to, popularity, drift) in profiles)
        {
            var route = routes.First(r => r.OriginStation.Code == from && r.DestinationStation.Code == to);

            // The 08:00 is the flagship departure and carries the route's history.
            var service = services.First(s => s.RouteId == route.Id && s.DepartureTime == new TimeOnly(8, 0));

            for (var weeksAgo = HistoryWeeks; weeksAgo >= 1; weeksAgo--)
            {
                foreach (var dayOffset in Enumerable.Range(0, 7))
                {
                    var date = today.AddDays(-weeksAgo * 7 + dayOffset);
                    if (date >= today || !service.RunsOn(date)) continue;

                    SeedServiceDate(service, route, date, popularity, drift, weeksAgo, demandUsers);
                }
            }
        }
    }

    /// <summary>Fills one service on one date up to its target occupancy.</summary>
    private void SeedServiceDate(
        TrainService service, Route route, DateOnly date,
        double popularity, double weeklyPriceDrift, int weeksAgo, List<int> demandUsers)
    {
        // How full the train ended up: day-of-week demand, scaled by how popular the route is,
        // plus a little noise. Clamped so nothing is empty or impossibly overfull.
        var occupancy = DayDemand[date.DayOfWeek] * popularity * (0.85 + _rng.NextDouble() * 0.30);
        occupancy = Math.Clamp(occupancy, 0.18, 0.97);

        var targetSeats = (int)Math.Round(service.Capacity * occupancy);

        // Prices drift week on week (the trend the chatbot reports), and peak days cost more.
        var weeksBeforeNow = weeksAgo;
        var trendFactor = 1.0 - weeklyPriceDrift * weeksBeforeNow; // older weeks sit further back along the trend
        var peakFactor = 0.90 + DayDemand[date.DayOfWeek] * 0.35;

        // Sell seats out of the real seat map, so a seeded booking occupies a seat the seat picker
        // actually draws — and never sells the same seat on the same train twice.
        var firstClassSeats = SeatMap.SeatsOfClass(service.Capacity, TravelClass.FirstClass);
        var standardSeats = SeatMap.SeatsOfClass(service.Capacity, TravelClass.Standard);

        var nextFirstClass = 0;
        var nextStandard = 0;
        var seatsSold = 0;

        while (seatsSold < targetSeats)
        {
            // Demand bookings are group-sized: they stand in for everyone who bought seats on this
            // train, so one booking commonly covers several travellers.
            var groupSize = Math.Min(_rng.Next(2, 16), targetSeats - seatsSold);

            var booking = new Booking
            {
                UserId = demandUsers[_rng.Next(demandUsers.Count)],
                TrainServiceId = service.Id,
                TravelDate = date,
                DateCreated = date.ToDateTime(TimeOnly.MinValue).AddDays(-_rng.Next(1, 30)),
                Status = BookingStatus.Completed,
                IsRecurring = false
            };

            for (var i = 0; i < groupSize; i++)
            {
                // Roughly one seat in six is first class — but only while first class has seats left.
                var wantsFirstClass = _rng.Next(6) == 0 && nextFirstClass < firstClassSeats.Count;

                (string Coach, int Number) seat;
                TravelClass travelClass;

                if (wantsFirstClass)
                {
                    seat = firstClassSeats[nextFirstClass++];
                    travelClass = TravelClass.FirstClass;
                }
                else if (nextStandard < standardSeats.Count)
                {
                    seat = standardSeats[nextStandard++];
                    travelClass = TravelClass.Standard;
                }
                else if (nextFirstClass < firstClassSeats.Count)
                {
                    // Standard sold out; the overflow spills into first class.
                    seat = firstClassSeats[nextFirstClass++];
                    travelClass = TravelClass.FirstClass;
                }
                else
                {
                    break; // train is genuinely full
                }

                // Per-seat noise on top of the route's trend and the day's peak factor.
                var demandFactor = (decimal)(trendFactor * peakFactor * (0.95 + _rng.NextDouble() * 0.12));

                booking.Seats.Add(new Seat
                {
                    Coach = seat.Coach,
                    SeatNumber = seat.Number,
                    TravelClass = travelClass,
                    Price = PricingCalculator.SeatPrice(route.DistanceKm, travelClass, demandFactor)
                });
            }

            if (booking.Seats.Count == 0) break; // no seats left to sell

            booking.TotalPrice = booking.CalculateTotal();
            _bookings.Add(booking);

            seatsSold += booking.Seats.Count;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The personal user's own bookings — what the CRUD screens, weekly view and report display
    // ---------------------------------------------------------------------------------------

    private void SeedPersonalBookings(User user, List<Route> routes, List<TrainService> services)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monday = StartOfWeek(today);

        // Past trips, so the report and history are not empty when the user pages backwards.
        var pastTrips = new (int WeeksAgo, DayOfWeek Day, string From, string To, TimeOnly Depart, int Seats)[]
        {
            (3, DayOfWeek.Tuesday, "KGX", "MAN", new TimeOnly(8, 0), 1),
            (3, DayOfWeek.Thursday, "MAN", "KGX", new TimeOnly(17, 15), 1),
            (2, DayOfWeek.Monday, "KGX", "LDS", new TimeOnly(6, 45), 2),
            (2, DayOfWeek.Friday, "LDS", "KGX", new TimeOnly(17, 15), 2),
            (1, DayOfWeek.Wednesday, "KGX", "BHM", new TimeOnly(12, 30), 1),
            (1, DayOfWeek.Friday, "KGX", "EDB", new TimeOnly(8, 0), 1)
        };

        foreach (var trip in pastTrips)
        {
            var date = StartOfWeek(today.AddDays(-trip.WeeksAgo * 7)).AddDays(DaysFromMonday(trip.Day));
            CreatePersonalBooking(user, routes, services, trip.From, trip.To, trip.Depart, date, trip.Seats,
                BookingStatus.Completed, requests: null);
        }

        // This week and next: enough to make the weekly view and report look alive.
        var upcoming = new (int DayOffsetFromMonday, string From, string To, TimeOnly Depart, int Seats, BookingStatus Status, RequestType? Request)[]
        {
            (1, "KGX", "MAN", new TimeOnly(8, 0), 1, BookingStatus.Confirmed, RequestType.QuietCoach),
            (2, "KGX", "BHM", new TimeOnly(12, 30), 2, BookingStatus.Confirmed, RequestType.ExtraLuggage),
            (4, "KGX", "LDS", new TimeOnly(17, 15), 1, BookingStatus.Confirmed, RequestType.Meal),
            (5, "KGX", "YRK", new TimeOnly(8, 0), 2, BookingStatus.Pending, RequestType.BicycleSpace),
            (8, "KGX", "EDB", new TimeOnly(8, 0), 1, BookingStatus.Confirmed, RequestType.WheelchairAccess),
            (9, "MAN", "LIV", new TimeOnly(12, 30), 1, BookingStatus.Confirmed, null),
            (11, "LDS", "KGX", new TimeOnly(17, 15), 1, BookingStatus.Pending, RequestType.Meal),
            (12, "KGX", "BRI", new TimeOnly(8, 0), 3, BookingStatus.Confirmed, null)
        };

        foreach (var trip in upcoming)
        {
            var date = monday.AddDays(trip.DayOffsetFromMonday);
            var requests = trip.Request is null
                ? null
                : new List<SpecialRequest> { BuildRequest(trip.Request.Value) };

            CreatePersonalBooking(user, routes, services, trip.From, trip.To, trip.Depart, date, trip.Seats,
                trip.Status, requests);
        }

        SeedRecurringBookings(user, routes, services, monday);
    }

    /// <summary>
    /// Recurring series. Occurrences are never stored — the weekly view expands the pattern on
    /// demand — so a commute that runs for months costs one Booking object, not sixty.
    /// </summary>
    private void SeedRecurringBookings(User user, List<Route> routes, List<TrainService> services, DateOnly monday)
    {
        // A weekly Monday/Wednesday commute into Manchester, running for the next 10 weeks.
        CreatePersonalBooking(
            user, routes, services, "KGX", "MAN", new TimeOnly(6, 45),
            travelDate: monday, seatCount: 1, status: BookingStatus.Confirmed,
            requests: new List<SpecialRequest> { BuildRequest(RequestType.QuietCoach) },
            recurrence: new RecurrencePattern
            {
                Frequency = Frequency.Weekly,
                Interval = 1,
                DaysOfWeek = new[] { System.DayOfWeek.Monday, System.DayOfWeek.Wednesday },
                StartDate = monday.AddDays(-14),
                EndDate = monday.AddDays(70)
            });

        // A fortnightly Friday trip to Leeds.
        CreatePersonalBooking(
            user, routes, services, "KGX", "LDS", new TimeOnly(17, 15),
            travelDate: monday.AddDays(4), seatCount: 1, status: BookingStatus.Confirmed,
            requests: null,
            recurrence: new RecurrencePattern
            {
                Frequency = Frequency.Weekly,
                Interval = 2,
                DaysOfWeek = new[] { System.DayOfWeek.Friday },
                StartDate = monday.AddDays(4),
                EndDate = monday.AddDays(84)
            });

        // A monthly visit to Edinburgh.
        CreatePersonalBooking(
            user, routes, services, "KGX", "EDB", new TimeOnly(8, 0),
            travelDate: monday.AddDays(5), seatCount: 2, status: BookingStatus.Confirmed,
            requests: new List<SpecialRequest> { BuildRequest(RequestType.Meal) },
            recurrence: new RecurrencePattern
            {
                Frequency = Frequency.Monthly,
                Interval = 1,
                DaysOfWeek = Array.Empty<DayOfWeek>(),
                StartDate = monday.AddDays(5),
                EndDate = monday.AddDays(150)
            });
    }

    private void CreatePersonalBooking(
        User user, List<Route> routes, List<TrainService> services,
        string from, string to, TimeOnly departure, DateOnly travelDate, int seatCount,
        BookingStatus status, List<SpecialRequest>? requests, RecurrencePattern? recurrence = null)
    {
        var route = routes.FirstOrDefault(r => r.OriginStation.Code == from && r.DestinationStation.Code == to);
        if (route is null) return;

        var service = services.FirstOrDefault(s => s.RouteId == route.Id && s.DepartureTime == departure)
                      ?? services.First(s => s.RouteId == route.Id);

        var booking = new Booking
        {
            UserId = user.Id,
            TrainServiceId = service.Id,
            TravelDate = travelDate,
            DateCreated = DateTime.Now.AddDays(-_rng.Next(3, 40)),
            Status = status,
            IsRecurring = recurrence is not null,
            RecurrencePattern = recurrence
        };

        for (var i = 0; i < seatCount; i++)
        {
            var travelClass = i == 0 && seatCount > 2 ? TravelClass.FirstClass : TravelClass.Standard;

            booking.Seats.Add(new Seat
            {
                Coach = travelClass == TravelClass.FirstClass ? "A" : "B",
                SeatNumber = 10 + i,
                TravelClass = travelClass,
                Price = PricingCalculator.SeatPrice(route.DistanceKm, travelClass)
            });
        }

        if (requests is not null)
            booking.SpecialRequests.AddRange(requests);

        booking.TotalPrice = booking.CalculateTotal();
        _bookings.Add(booking);
    }

    private static SpecialRequest BuildRequest(RequestType type) => new()
    {
        Type = type,
        Status = RequestStatus.Requested,
        Description = type switch
        {
            RequestType.WheelchairAccess => "Wheelchair space and ramp assistance at both ends.",
            RequestType.Meal => "Vegetarian meal, no nuts.",
            RequestType.ExtraLuggage => "One oversized suitcase.",
            RequestType.QuietCoach => "Seat in the quiet coach, please.",
            RequestType.BicycleSpace => "Full-size bicycle, reserved space required.",
            _ => string.Empty
        }
    };

    /// <summary>Monday of the week containing <paramref name="date"/>.</summary>
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek - (int)System.DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }

    /// <summary>Days from Monday for a given day of week (Monday = 0 … Sunday = 6).</summary>
    private static int DaysFromMonday(DayOfWeek day) => ((int)day - (int)System.DayOfWeek.Monday + 7) % 7;
}
