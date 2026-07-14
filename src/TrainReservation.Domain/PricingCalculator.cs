namespace TrainReservation.Domain;

/// <summary>
/// Fare rules for the network. Kept in the domain so the seeder, the booking flow and the
/// prediction engine all price seats the same way — the chatbot's projections would be
/// meaningless if historical prices came from different rules than new bookings.
/// </summary>
public static class PricingCalculator
{
    public const decimal BaseFee = 5.00m;
    public const decimal RatePerKm = 0.12m;
    public const decimal FirstClassMultiplier = 1.65m;

    /// <summary>
    /// Price of one seat. <paramref name="demandFactor"/> represents market conditions
    /// (peak days, how far ahead the seat was bought); 1.0 is the flat fare.
    /// </summary>
    public static decimal SeatPrice(int distanceKm, TravelClass travelClass, decimal demandFactor = 1.0m)
    {
        var price = BaseFee + distanceKm * RatePerKm;

        if (travelClass == TravelClass.FirstClass)
            price *= FirstClassMultiplier;

        return Math.Round(price * demandFactor, 2, MidpointRounding.AwayFromZero);
    }
}
