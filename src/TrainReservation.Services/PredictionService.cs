using System.Text;
using TrainReservation.Data.Repositories;
using TrainReservation.Domain;
using TrainReservation.Services.Models;

namespace TrainReservation.Services;

/// <summary>
/// The prediction chatbot.
///
/// Everything here is computed from the seeded booking history held in memory — there is no model,
/// no training and no network call. Two questions get answered:
///
///  • <b>Pricing</b> — a moving average of what seats on this route have actually sold for, split
///    into a recent and an older window to get a trend direction, then projected forward to the
///    target date and adjusted for how busy that day of the week usually is.
///
///  • <b>Availability</b> — historical occupancy (seats sold / capacity) for this route on this day
///    of the week, classified into likely available / limited / likely full.
///
/// Both carry a confidence derived from how much history actually backs them.
/// </summary>
public class PredictionService : IPredictionService
{
    /// <summary>Weeks of history considered "recent" when working out the trend.</summary>
    private const int RecentWindowWeeks = 4;

    /// <summary>Total history the moving average looks at.</summary>
    private const int HistoryWindowWeeks = 12;

    // Occupancy thresholds for the availability verdict.
    private const double LimitedThreshold = 0.65;
    private const double FullThreshold = 0.85;

    private readonly IBookingRepository _bookings;
    private readonly ITrainServiceRepository _services;
    private readonly IRouteRepository _routes;
    private readonly IStationRepository _stations;

    public PredictionService(
        IBookingRepository bookings,
        ITrainServiceRepository services,
        IRouteRepository routes,
        IStationRepository stations)
    {
        _bookings = bookings;
        _services = services;
        _routes = routes;
        _stations = stations;
    }

    // -----------------------------------------------------------------------------------------
    // Entry points
    // -----------------------------------------------------------------------------------------

    public ChatResponse Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return Clarify("Ask me about a route and a date — for example, \"What will a London to Manchester ticket cost next Friday?\"");

        var parser = new QuestionParser(_stations.GetAll().ToList());
        var parsed = parser.Parse(question);

        var route = ResolveRoute(parsed, out var clarification);
        if (route is null) return Clarify(clarification!);

        var service = ResolveService(route, parsed);
        var response = Predict(route.Id, parsed.Date!.Value, service?.Id);

        // If the date was only assumed, say so up front rather than quietly answering the wrong question.
        if (parsed.DateWasAssumed && response.Prediction is not null)
        {
            var note = $"You didn't say when, so I've assumed **{parsed.Date:dddd d MMMM}**. Pick a date below to change that.\n\n";
            return new ChatResponse
            {
                Message = note + response.Message,
                Prediction = response.Prediction
            };
        }

        return response;
    }

    public ChatResponse Predict(int routeId, DateOnly date, int? trainServiceId = null)
    {
        var route = _routes.GetById(routeId);
        if (route is null)
            return Clarify("I don't know that route. Choose one from the list below.");

        var service = trainServiceId is null ? null : _services.GetById(trainServiceId.Value);

        var history = GetHistory(route, service);
        var prediction = BuildPrediction(route, service, date, history);

        return new ChatResponse
        {
            Message = Narrate(prediction, history),
            Prediction = prediction
        };
    }

    // -----------------------------------------------------------------------------------------
    // Resolving what the user meant
    // -----------------------------------------------------------------------------------------

    private Route? ResolveRoute(ParsedQuestion parsed, out string? clarification)
    {
        clarification = null;

        // Both ends given: look the route up directly, and fall back to the reverse direction so
        // "Manchester to London" still works if only the southbound leg happens to be modelled.
        if (parsed.Origin is not null && parsed.Destination is not null)
        {
            var route = _routes.FindByStations(parsed.Origin.Code, parsed.Destination.Code)
                        ?? _routes.FindByStations(parsed.Destination.Code, parsed.Origin.Code);

            if (route is not null) return route;

            clarification = $"I don't have a route between **{parsed.Origin.City}** and **{parsed.Destination.City}**. " +
                            "Pick a route from the dropdown below and I'll predict for that.";
            return null;
        }

        // Only one end given ("will there be seats to Leeds?"). If exactly one route serves it,
        // that must be the one they meant; otherwise ask which.
        var single = parsed.Destination ?? parsed.Origin;

        if (single is not null)
        {
            var matches = (parsed.Destination is not null
                    ? _routes.GetAll().Where(r => r.DestinationStation.Code == single.Code)
                    : _routes.GetAll().Where(r => r.OriginStation.Code == single.Code))
                .ToList();

            if (matches.Count == 1) return matches[0];

            if (matches.Count > 1)
            {
                var options = string.Join(", ", matches.Select(r => r.Description));
                clarification = $"Which route to **{single.City}** did you mean? I have: {options}. " +
                                "You can also pick one from the dropdown below.";
                return null;
            }
        }

        clarification = "I couldn't work out which route you meant. Try naming both ends — " +
                        "for example, \"London to Leeds next Monday\" — or pick a route below.";
        return null;
    }

    /// <summary>Ties "the 08:00" in the question to an actual service on the route.</summary>
    private TrainService? ResolveService(Route route, ParsedQuestion parsed)
    {
        if (parsed.Time is null) return null;

        return _services.GetByRoute(route.Id)
            .FirstOrDefault(s => s.DepartureTime == parsed.Time.Value);
    }

    // -----------------------------------------------------------------------------------------
    // The evidence
    // -----------------------------------------------------------------------------------------

    /// <summary>One historical service-date: what it sold for and how full it got.</summary>
    private record HistoricalSample(DateOnly Date, int WeeksAgo, decimal AverageStandardPrice, int SeatsSold, int Capacity)
    {
        public double Occupancy => Capacity == 0 ? 0 : (double)SeatsSold / Capacity;
    }

    /// <summary>
    /// Every past service-date on this route (optionally narrowed to one departure), rolled up from
    /// the individual bookings into one sample per train.
    /// </summary>
    private List<HistoricalSample> GetHistory(Route route, TrainService? service)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var earliest = today.AddDays(-HistoryWindowWeeks * 7);

        var serviceIds = (service is not null
                ? new[] { service }
                : _services.GetByRoute(route.Id).ToArray())
            .ToDictionary(s => s.Id, s => s.Capacity);

        // Group the raw bookings back into "one train on one day".
        var samples = _bookings.GetAll()
            .Where(b => b.Status != BookingStatus.Cancelled)
            .Where(b => !b.IsRecurring)
            .Where(b => serviceIds.ContainsKey(b.TrainServiceId))
            .Where(b => b.TravelDate < today && b.TravelDate >= earliest)
            .GroupBy(b => new { b.TrainServiceId, b.TravelDate })
            .Select(g =>
            {
                var seats = g.SelectMany(b => b.Seats).ToList();

                // Price the standard-class seats only: mixing first class into the average would
                // make the headline figure swing on carriage mix rather than on demand.
                var standard = seats.Where(s => s.TravelClass == TravelClass.Standard).ToList();
                var priced = standard.Count > 0 ? standard : seats;

                return new HistoricalSample(
                    Date: g.Key.TravelDate,
                    WeeksAgo: (today.DayNumber - g.Key.TravelDate.DayNumber) / 7,
                    AverageStandardPrice: priced.Count == 0 ? 0 : Math.Round(priced.Average(s => s.Price), 2),
                    SeatsSold: seats.Count,
                    Capacity: serviceIds[g.Key.TrainServiceId]);
            })
            .Where(s => s.SeatsSold > 0)
            .OrderBy(s => s.Date)
            .ToList();

        return samples;
    }

    private Prediction BuildPrediction(Route route, TrainService? service, DateOnly targetDate, List<HistoricalSample> history)
    {
        var flatFare = PricingCalculator.SeatPrice(route.DistanceKm, TravelClass.Standard);

        // No history at all: fall back to the published fare and say confidence is low.
        if (history.Count == 0)
        {
            var capacity = service?.Capacity ?? 110;

            return new Prediction
            {
                Route = route,
                Service = service,
                TargetDate = targetDate,
                ProjectedPrice = flatFare,
                RecentAveragePrice = flatFare,
                OlderAveragePrice = flatFare,
                Trend = TrendDirection.Stable,
                TrendPercent = 0,
                Availability = AvailabilityLevel.LikelyAvailable,
                AverageOccupancy = 0,
                TypicalSeatsFree = capacity,
                SampleSize = 0,
                Confidence = ConfidenceLevel.Low
            };
        }

        // ---- Pricing: moving average, split recent vs older to get a direction ----------------

        var recent = history.Where(s => s.WeeksAgo < RecentWindowWeeks).ToList();
        var older = history.Where(s => s.WeeksAgo >= RecentWindowWeeks).ToList();

        // If one window is empty (a thin history), lean on whatever we do have.
        var recentAvg = recent.Count > 0 ? recent.Average(s => s.AverageStandardPrice) : history.Average(s => s.AverageStandardPrice);
        var olderAvg = older.Count > 0 ? older.Average(s => s.AverageStandardPrice) : recentAvg;

        var trendPercent = olderAvg == 0 ? 0 : (recentAvg - olderAvg) / olderAvg * 100m;

        var trend = trendPercent switch
        {
            > 2m => TrendDirection.Rising,
            < -2m => TrendDirection.Easing,
            _ => TrendDirection.Stable
        };

        // Project forward. The two windows are centred roughly six weeks apart, so the observed
        // change spread over six weeks gives a per-week drift we can carry to the target date.
        var weeksAhead = Math.Max(0, (targetDate.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber) / 7.0);
        var weeklyDrift = trendPercent / 100m / 6m;

        var projected = recentAvg * (1m + weeklyDrift * (decimal)(weeksAhead + 2));

        // Busy days cost more. Scale by how this day of week has priced against the route's average.
        var dayFactor = DayOfWeekPriceFactor(history, targetDate.DayOfWeek);
        projected *= dayFactor;

        // Keep the projection tethered to observed reality — a long extrapolation should not run away.
        projected = Math.Clamp(projected, recentAvg * 0.70m, recentAvg * 1.50m);

        // ---- Availability: occupancy on this day of the week ------------------------------------

        var sameDay = history.Where(s => s.Date.DayOfWeek == targetDate.DayOfWeek).ToList();
        var occupancySamples = sameDay.Count > 0 ? sameDay : history;

        var avgOccupancy = occupancySamples.Average(s => s.Occupancy);

        var availability = avgOccupancy switch
        {
            >= FullThreshold => AvailabilityLevel.LikelyFull,
            >= LimitedThreshold => AvailabilityLevel.Limited,
            _ => AvailabilityLevel.LikelyAvailable
        };

        var capacityForTarget = service?.Capacity ?? (int)Math.Round(occupancySamples.Average(s => s.Capacity));
        var typicalFree = Math.Max(0, (int)Math.Round(capacityForTarget * (1 - avgOccupancy)));

        // Confidence tracks how many like-for-like days we actually observed.
        var sampleSize = sameDay.Count;

        var confidence = sampleSize switch
        {
            >= 8 => ConfidenceLevel.High,
            >= 4 => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.Low
        };

        return new Prediction
        {
            Route = route,
            Service = service,
            TargetDate = targetDate,
            ProjectedPrice = Math.Round(projected, 2),
            RecentAveragePrice = Math.Round(recentAvg, 2),
            OlderAveragePrice = Math.Round(olderAvg, 2),
            Trend = trend,
            TrendPercent = Math.Round(trendPercent, 1),
            Availability = availability,
            AverageOccupancy = avgOccupancy,
            TypicalSeatsFree = typicalFree,
            SampleSize = sampleSize,
            Confidence = confidence
        };
    }

    /// <summary>
    /// How this day of the week has priced relative to the route overall. Returns 1.0 when there is
    /// nothing to go on, so a thin history simply leaves the projection unadjusted.
    /// </summary>
    private static decimal DayOfWeekPriceFactor(List<HistoricalSample> history, DayOfWeek day)
    {
        var overall = history.Average(s => s.AverageStandardPrice);
        if (overall == 0) return 1m;

        var onDay = history.Where(s => s.Date.DayOfWeek == day).ToList();
        if (onDay.Count == 0) return 1m;

        var factor = onDay.Average(s => s.AverageStandardPrice) / overall;

        // Guard against a single outlier day skewing the whole projection.
        return Math.Clamp(factor, 0.85m, 1.20m);
    }

    // -----------------------------------------------------------------------------------------
    // Turning the numbers into prose
    // -----------------------------------------------------------------------------------------

    private string Narrate(Prediction p, List<HistoricalSample> history)
    {
        var sb = new StringBuilder();
        var route = p.Route!;

        var when = $"{p.TargetDate:dddd d MMMM}";
        // A named service is singular ("the 08:00 ... runs"); the route as a whole is plural
        // ("services ... run"), so the verb has to agree with whichever we ended up with.
        var serviceLabel = p.Service is not null
            ? $"the {p.Service.DepartureTime:HH:mm} ({p.Service.TrainNumber})"
            : "services";

        var verb = p.Service is not null ? "runs" : "run";

        if (p.SampleSize == 0 && history.Count == 0)
        {
            sb.AppendLine($"I have **no booking history** for {route.Description}, so this is a best-effort estimate only.");
            sb.AppendLine();
            sb.AppendLine($"Going on the published fare, a standard seat on {when} should cost around **£{p.ProjectedPrice:0.00}**, " +
                          "and with nothing booked I'd expect seats to be **available**.");
            sb.AppendLine();
            sb.AppendLine("_Confidence: low — there's no history for this route yet._");
            return sb.ToString();
        }

        // Pricing
        var trendWord = p.Trend switch
        {
            TrendDirection.Rising => $"**rising** ({p.TrendPercent:+0.0;-0.0}% over the last month)",
            TrendDirection.Easing => $"**easing** ({p.TrendPercent:+0.0;-0.0}% over the last month)",
            _ => "**broadly flat**"
        };

        sb.AppendLine($"**{route.Description}** — {when}");
        sb.AppendLine();
        sb.AppendLine($"A standard seat should cost around **£{p.ProjectedPrice:0.00}**.");
        sb.AppendLine();
        sb.AppendLine($"Recent seats on this route have averaged **£{p.RecentAveragePrice:0.00}** " +
                      $"(against **£{p.OlderAveragePrice:0.00}** earlier in the quarter), so prices are {trendWord}.");
        sb.AppendLine();

        // Availability
        var occupancyPercent = p.AverageOccupancy * 100;

        var verdict = p.Availability switch
        {
            AvailabilityLevel.LikelyFull =>
                $"**Likely full.** {serviceLabel.FirstUpper()} on a {p.TargetDate.DayOfWeek} typically {verb} **{occupancyPercent:0}% full**, " +
                $"leaving only about **{p.TypicalSeatsFree} seats**. Book early if you need this one.",

            AvailabilityLevel.Limited =>
                $"**Limited.** {serviceLabel.FirstUpper()} on a {p.TargetDate.DayOfWeek} typically {verb} **{occupancyPercent:0}% full**, " +
                $"so expect roughly **{p.TypicalSeatsFree} seats** left. Worth booking ahead.",

            _ =>
                $"**Likely available.** {serviceLabel.FirstUpper()} on a {p.TargetDate.DayOfWeek} typically {verb} **{occupancyPercent:0}% full**, " +
                $"leaving about **{p.TypicalSeatsFree} seats** free."
        };

        sb.AppendLine(verdict);
        sb.AppendLine();

        var confidenceNote = p.Confidence switch
        {
            ConfidenceLevel.High => $"high — based on {p.SampleSize} previous {p.TargetDate.DayOfWeek}s on this route",
            ConfidenceLevel.Medium => $"medium — based on {p.SampleSize} previous {p.TargetDate.DayOfWeek}s on this route",
            _ => p.SampleSize == 0
                ? "low — I have no history for this route on a " + p.TargetDate.DayOfWeek + ", so I've fallen back on the route average"
                : $"low — only {p.SampleSize} previous {p.TargetDate.DayOfWeek}(s) to go on"
        };

        sb.AppendLine($"_Confidence: {confidenceNote}._");

        return sb.ToString();
    }

    private static ChatResponse Clarify(string message) => new()
    {
        Message = message,
        NeedsClarification = true
    };
}

internal static class StringExtensions
{
    /// <summary>Capitalises the first letter, for sentences that begin with a generated label.</summary>
    public static string FirstUpper(this string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
