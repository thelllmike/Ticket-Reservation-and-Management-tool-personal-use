namespace TrainReservation.Domain;

/// <summary>
/// The physical layout of a train: which coaches exist, what class they are, and how many seats
/// each holds.
///
/// This lives in the domain because two very different callers have to agree on it — the seat
/// picker in the UI draws the grid from it, and the seeder places its historical bookings into it.
/// If they disagreed, a seat shown as free could already be sold.
/// </summary>
public static class SeatMap
{
    /// <summary>One coach: its letter, its class, and how many seats it has.</summary>
    public record Coach(string Letter, TravelClass TravelClass, int SeatCount);

    /// <summary>
    /// Coach A is first class (about a fifth of the train); B and C split the standard seats.
    /// </summary>
    public static IReadOnlyList<Coach> Coaches(int capacity)
    {
        var first = Math.Max(4, (int)Math.Round(capacity * 0.2));
        var remaining = Math.Max(0, capacity - first);

        var b = (int)Math.Ceiling(remaining / 2.0);
        var c = remaining - b;

        return new List<Coach>
        {
            new("A", TravelClass.FirstClass, first),
            new("B", TravelClass.Standard, b),
            new("C", TravelClass.Standard, c)
        };
    }

    /// <summary>Every seat on a train of the given capacity, in coach order.</summary>
    public static IEnumerable<(string Coach, int Number, TravelClass TravelClass)> AllSeats(int capacity)
    {
        foreach (var coach in Coaches(capacity))
            for (var n = 1; n <= coach.SeatCount; n++)
                yield return (coach.Letter, n, coach.TravelClass);
    }

    /// <summary>Every seat of one class, used by the seeder to place bookings realistically.</summary>
    public static List<(string Coach, int Number)> SeatsOfClass(int capacity, TravelClass travelClass) =>
        AllSeats(capacity)
            .Where(s => s.TravelClass == travelClass)
            .Select(s => (s.Coach, s.Number))
            .ToList();

    public static string Label(string coach, int number) => $"{coach}{number}";
}
