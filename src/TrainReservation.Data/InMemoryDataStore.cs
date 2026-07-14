using System.Collections.Concurrent;
using TrainReservation.Domain;

namespace TrainReservation.Data;

/// <summary>
/// The single place in the whole application that knows storage is in-memory.
///
/// Every entity type lives in a <see cref="Dictionary{TKey,TValue}"/> keyed by its id, giving
/// O(1) lookup; ordered results are produced with LINQ at query time. Registered as a
/// singleton so state survives across requests, and therefore guarded by <see cref="Sync"/> —
/// ASP.NET Core serves requests concurrently, so the dictionaries would otherwise be racy.
///
/// Swapping this for a database means reimplementing the repositories against a DbContext;
/// nothing above the Data layer changes.
/// </summary>
public sealed class InMemoryDataStore
{
    /// <summary>Single lock guarding every dictionary below. Repositories must hold it while reading or writing.</summary>
    public object Sync { get; } = new();

    /// <summary>Stations are keyed by their natural key (the station code), not an int id.</summary>
    public Dictionary<string, Station> Stations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<int, User> Users { get; } = new();
    public Dictionary<int, Route> Routes { get; } = new();
    public Dictionary<int, TrainService> TrainServices { get; } = new();
    public Dictionary<int, Booking> Bookings { get; } = new();

    /// <summary>
    /// Seats and special requests are owned by their booking, but are also indexed by id here so
    /// they can be fetched and edited directly. Both collections hold the *same object references*
    /// that sit in <see cref="Booking.Seats"/> / <see cref="Booking.SpecialRequests"/>, so there is
    /// only ever one source of truth — these are indexes, not copies.
    /// </summary>
    public Dictionary<int, Seat> Seats { get; } = new();

    public Dictionary<int, SpecialRequest> SpecialRequests { get; } = new();

    // ---- Id generation -------------------------------------------------------------------
    // Interlocked keeps ids unique even if a caller generates one outside the lock.

    private int _userId;
    private int _routeId;
    private int _trainServiceId;
    private int _bookingId;
    private int _seatId;
    private int _specialRequestId;
    private int _recurrencePatternId;

    public int NextUserId() => Interlocked.Increment(ref _userId);
    public int NextRouteId() => Interlocked.Increment(ref _routeId);
    public int NextTrainServiceId() => Interlocked.Increment(ref _trainServiceId);
    public int NextBookingId() => Interlocked.Increment(ref _bookingId);
    public int NextSeatId() => Interlocked.Increment(ref _seatId);
    public int NextSpecialRequestId() => Interlocked.Increment(ref _specialRequestId);
    public int NextRecurrencePatternId() => Interlocked.Increment(ref _recurrencePatternId);

    /// <summary>Booking references are human-facing, e.g. "TKT-10042".</summary>
    public string NextBookingReference(int bookingId) => $"TKT-{10000 + bookingId}";
}
