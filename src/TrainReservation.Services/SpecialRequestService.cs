using TrainReservation.Data.Repositories;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Services;

/// <summary>
/// CRUD over the assistance requests attached to bookings. A request cannot exist on its own —
/// it always belongs to exactly one booking.
/// </summary>
public class SpecialRequestService : ISpecialRequestService
{
    private readonly ISpecialRequestRepository _requests;
    private readonly IBookingRepository _bookings;
    private readonly IUserRepository _users;

    public SpecialRequestService(
        ISpecialRequestRepository requests,
        IBookingRepository bookings,
        IUserRepository users)
    {
        _requests = requests;
        _bookings = bookings;
        _users = users;
    }

    public IEnumerable<SpecialRequest> GetAll()
    {
        var me = _users.GetCurrentUser().Id;

        // Only the personal user's requests: the seeded network history carries none, but filtering
        // here keeps the page correct if that ever changes.
        var myBookingIds = _bookings.GetAll()
            .Where(b => b.UserId == me)
            .Select(b => b.Id)
            .ToHashSet();

        return _requests.GetAll()
            .Where(r => myBookingIds.Contains(r.BookingId))
            .OrderBy(r => r.Status)
            .ThenBy(r => r.Type);
    }

    public SpecialRequest? Get(int id) => _requests.GetById(id);

    public Booking? GetOwningBooking(int requestId)
    {
        var request = _requests.GetById(requestId);
        return request is null ? null : _bookings.GetById(request.BookingId);
    }

    public OperationResult Create(int bookingId, SpecialRequest request)
    {
        if (_bookings.GetById(bookingId) is null)
            return OperationResult.Fail("Choose the booking this request belongs to.");

        var created = _requests.AddToBooking(bookingId, request);
        return OperationResult.Ok(created.Id);
    }

    public OperationResult Update(SpecialRequest request)
    {
        if (_requests.GetById(request.Id) is null)
            return OperationResult.Fail("That request no longer exists.");

        return _requests.Update(request)
            ? OperationResult.Ok(request.Id)
            : OperationResult.Fail("Could not update the request.");
    }

    public OperationResult Delete(int id) =>
        _requests.Delete(id)
            ? OperationResult.Ok()
            : OperationResult.Fail("That request no longer exists.");
}
